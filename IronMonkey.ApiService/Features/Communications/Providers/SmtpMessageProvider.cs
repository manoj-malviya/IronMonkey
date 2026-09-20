using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using IronMonkey.Data.Communications;

namespace IronMonkey.ApiService.Features.Communications.Providers;

/// <summary>
/// Sends email over SMTP.
///
/// Replaces the two dead classes this feature removed, and fixes what was wrong with them:
/// TLS is on rather than commented out, the send is awaited rather than fired and forgotten,
/// and the outcome is reported from what the server actually said rather than logged as
/// success unconditionally.
/// </summary>
public sealed class SmtpMessageProvider(
    IOptions<CommunicationsOptions> options,
    ILogger<SmtpMessageProvider> logger) : IMessageProvider
{
    private readonly CommunicationsOptions _options = options.Value;
    private SmtpProviderOptions Smtp => _options.Smtp;

    public MessageChannel Channel => MessageChannel.Email;

    public string Name => "Smtp";

    public bool IsConfigured => Smtp.IsConfigured;

    public string FromAddress => Smtp.FromAddress ?? string.Empty;

    public async Task<SendResult> SendAsync(OutboundMessageRequest request, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return SendResult.Permanent(MessageErrorCategory.ChannelNotConfigured, "SMTP is not configured.");

        MailAddress from;
        MailAddress to;
        try
        {
            from = string.IsNullOrWhiteSpace(Smtp.FromName)
                ? new MailAddress(Smtp.FromAddress!)
                : new MailAddress(Smtp.FromAddress!, Smtp.FromName);

            to = new MailAddress(request.To);
        }
        catch (FormatException ex)
        {
            // A malformed address is the sender's data problem and will fail identically on
            // every retry, so it is permanent.
            return SendResult.Permanent(MessageErrorCategory.InvalidRecipient, ex.Message);
        }

        using var client = new SmtpClient(Smtp.Host, Smtp.Port)
        {
            EnableSsl = Smtp.UseSsl,
            Timeout = _options.ProviderTimeoutSeconds * 1000
        };

        // Only attach credentials when there are some. An empty NetworkCredential makes some
        // servers refuse an otherwise-valid anonymous relay.
        if (!string.IsNullOrWhiteSpace(Smtp.Username))
            client.Credentials = new NetworkCredential(Smtp.Username, Smtp.Password);

        using var mail = new MailMessage(from, to)
        {
            Subject = request.Subject ?? string.Empty,
            Body = request.Body,
            IsBodyHtml = request.IsBodyHtml
        };

        // SMTP has no id of its own to return, so the idempotency key doubles as the
        // Message-ID. That makes a bounce traceable back to the row that produced it.
        mail.Headers.Add("X-IronMonkey-Idempotency-Key", request.IdempotencyKey);

        try
        {
            await client.SendMailAsync(mail, cancellationToken);
            return SendResult.Success(request.IdempotencyKey);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The worker is shutting down, not the provider failing. Retryable.
            return SendResult.Transient(MessageErrorCategory.ProviderTimeout, "The send was cancelled before it completed.");
        }
        catch (SmtpFailedRecipientException ex)
        {
            // The server named this recipient as the problem — retrying delivers it to the
            // same rejecting address.
            logger.LogWarning(ex, "SMTP rejected recipient for message {Key}", request.IdempotencyKey);
            return SendResult.Permanent(MessageErrorCategory.InvalidRecipient, ex.Message);
        }
        catch (SmtpException ex)
        {
            logger.LogWarning(ex, "SMTP send failed for message {Key} with status {Status}",
                request.IdempotencyKey, ex.StatusCode);

            return Classify(ex);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
        {
            logger.LogWarning(ex, "SMTP transport failure for message {Key}", request.IdempotencyKey);
            return SendResult.Transient(MessageErrorCategory.ProviderNetworkFailure, ex.Message);
        }
    }

    /// <summary>
    /// Maps an SMTP status onto the retry decision.
    ///
    /// The split matters: a mailbox that is full clears, while a mailbox that does not exist
    /// never will. Retrying the latter three times just delays the failure the user needs to
    /// see, and on a large send burns quota.
    /// </summary>
    private static SendResult Classify(SmtpException ex) => ex.StatusCode switch
    {
        SmtpStatusCode.MailboxBusy
            or SmtpStatusCode.MailboxUnavailable
            or SmtpStatusCode.InsufficientStorage
            or SmtpStatusCode.TransactionFailed
            or SmtpStatusCode.ServiceNotAvailable
            or SmtpStatusCode.LocalErrorInProcessing =>
            SendResult.Transient(MessageErrorCategory.ProviderNetworkFailure, ex.Message),

        SmtpStatusCode.GeneralFailure =>
            // GeneralFailure is what a connection-level failure surfaces as, so it is treated
            // as transport rather than as a server refusal.
            SendResult.Transient(MessageErrorCategory.ProviderNetworkFailure, ex.Message),

        SmtpStatusCode.ClientNotPermitted
            or SmtpStatusCode.MustIssueStartTlsFirst =>
            SendResult.Permanent(MessageErrorCategory.ProviderRejected, ex.Message),

        _ => SendResult.Permanent(MessageErrorCategory.ProviderRejected, ex.Message)
    };
}
