using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Features.Communications;
using IronMonkey.Data;
using IronMonkey.Data.Communications;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.UserManagement.Invitations;

/// <summary>Result of issuing or resending, including the one-time link for the admin.</summary>
public sealed record InvitationIssued(
    TeamInvitation Invitation,
    string AcceptUrl,
    bool Delivered,
    string? DeliveryError);

public interface IInvitationService
{
    Task<InvitationIssued> DeliverAsync(
        TenantDbContext db,
        TeamInvitation invitation,
        string plaintextToken,
        string tenantName,
        CancellationToken cancellationToken);
}

/// <summary>
/// Sends the invitation notification and reports honestly whether it went out.
///
/// Delivery goes through <see cref="IMessageDispatcher"/>, the one path every outbound
/// message takes, so an invitation is subject to the same consent and channel rules as
/// anything else the tenant sends — an address that opted out is not re-mailed just because
/// the message happens to be an invitation.
///
/// A refusal is NOT an exception. An unconfigured provider reports Rejected /
/// ChannelNotConfigured, and this surfaces that as the invitation's Failed state with the
/// link still valid. That is deliberate: local dev has no mail credentials, and an admin who
/// can copy the link is not blocked by a missing SMTP server.
/// </summary>
public sealed class InvitationService(
    IMessageDispatcher dispatcher,
    IConfiguration configuration,
    ILogger<InvitationService> logger) : IInvitationService
{
    public async Task<InvitationIssued> DeliverAsync(
        TenantDbContext db,
        TeamInvitation invitation,
        string plaintextToken,
        string tenantName,
        CancellationToken cancellationToken)
    {
        var acceptUrl = BuildAcceptUrl(invitation.TenantId, plaintextToken);

        string? error;
        bool delivered;

        try
        {
            var outcome = await dispatcher.QueueAsync(
                db,
                new SendMessageCommand(
                    TenantId: invitation.TenantId,
                    Channel: MessageChannel.Email,
                    To: invitation.Email,
                    Subject: $"You have been invited to join {tenantName}",
                    Body: BuildBody(invitation, tenantName, acceptUrl),
                    // Keyed on the invitation and its send count, both of which live on the
                    // row itself. A Hangfire retry of the same send reuses the key and stops
                    // at the duplicate guard; a genuine resend has a higher SendCount and so
                    // is a different message. Nothing here reads an optional audit object,
                    // which is what silently changed the key and double-sent before.
                    IdempotencyKey: $"invite:{invitation.Id:N}:{invitation.SendCount}",
                    SentByUserId: invitation.InvitedByUserId),
                cancellationToken);

            // Queued is the happy path: the dispatcher never contacts a provider inline.
            // Rejected is terminal and actionable — almost always no configured channel.
            delivered = outcome.Queued || outcome.Accepted;
            error = delivered ? null : (outcome.ErrorMessage ?? outcome.ErrorCategory.ToString());
        }
        catch (Exception ex)
        {
            // An invitation that exists but could not be announced is still a usable
            // invitation. Failing the whole request here would roll back a perfectly good
            // token and leave the admin with nothing to copy.
            logger.LogWarning(ex, "Invitation {InvitationId} could not be queued for delivery.", invitation.Id);
            delivered = false;
            error = "The invitation email could not be sent.";
        }

        if (!delivered)
            invitation.MarkDeliveryFailed(Truncate(error!, 500), DateTime.UtcNow);

        return new InvitationIssued(invitation, acceptUrl, delivered, error);
    }

    /// <summary>
    /// The link the invitee follows. The tenant id travels in the URL alongside the token so
    /// the anonymous acceptance endpoint knows which database to look in — it is a routing
    /// value, not an authorisation one: the token still has to verify against a row *in that
    /// tenant*, so naming a different tenant simply fails to match.
    /// </summary>
    private string BuildAcceptUrl(Guid tenantId, string token)
    {
        var baseUrl = (configuration["Web:BaseUrl"] ?? "https://localhost:7080").TrimEnd('/');
        return $"{baseUrl}/invitations/accept?tenant={tenantId}&token={Uri.EscapeDataString(token)}";
    }

    private static string BuildBody(TeamInvitation invitation, string tenantName, string acceptUrl)
    {
        // Every interpolated value is attacker-influenced to some degree — the invitee's
        // name is typed by an admin, the tenant name came from a signup form — and this is
        // an HTML email, so all of it is escaped.
        var name = System.Net.WebUtility.HtmlEncode(invitation.Name);
        var team = System.Net.WebUtility.HtmlEncode(tenantName);
        var url = System.Net.WebUtility.HtmlEncode(acceptUrl);

        var note = string.IsNullOrWhiteSpace(invitation.Message)
            ? string.Empty
            : $"<p><em>{System.Net.WebUtility.HtmlEncode(invitation.Message)}</em></p>";

        return $"""
            <p>Hi {name},</p>
            <p>You have been invited to join <strong>{team}</strong>.</p>
            {note}
            <p><a href="{url}">Accept your invitation</a> and choose a password.</p>
            <p>This link expires on {invitation.ExpiresAt:yyyy-MM-dd HH:mm} UTC and can be used once.</p>
            """;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
