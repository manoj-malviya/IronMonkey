using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace IronMonkey.ApiService.Common.Services;

public interface IEmailService
{
    Task SendWriterInvitationAsync(string toEmail, string publisherName, string? message, string invitationLink);
}

public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;
    private readonly SmtpSettings _smtpSettings;

    public EmailService(ILogger<EmailService> logger, IOptions<SmtpSettings> smtpSettings)
    {
        _logger = logger;
        _smtpSettings = smtpSettings.Value;
    }

    public async Task SendWriterInvitationAsync(string toEmail, string publisherName, string? message, string invitationLink)
    {
        try
        {
            using var client = new SmtpClient(_smtpSettings.Server, _smtpSettings.Port)
            {
                // EnableSsl = true,
                Credentials = new System.Net.NetworkCredential(_smtpSettings.Username, _smtpSettings.Password)
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(_smtpSettings.FromEmail, _smtpSettings.FromName),
                Subject = $"{publisherName} has invited you to join them as a writer on IronMonkey!",
                Body = GenerateInvitationEmailBody(publisherName, message, invitationLink),
                IsBodyHtml = true
            };
            mailMessage.To.Add(toEmail);

            await client.SendMailAsync(mailMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send writer invitation email to {Email}", toEmail);
            throw;
        }
    }

    private string GenerateInvitationEmailBody(string publisherName, string? message, string invitationLink)
    {
        return $@"
            <html>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: #2c3e50;'>You've Been Invited to Join IronMonkey!</h2>
                        <p><strong>{publisherName}</strong> has invited you to join them as a writer on IronMonkey.</p>
                        {(message != null ? $"<p>Message from {publisherName}:<br><em>{message}</em></p>" : "")}
                        <p>Click the button below to accept the invitation and get started:</p>
                        <div style='text-align: center; margin: 30px 0;'>
                            <a href='{invitationLink}' 
                               style='background-color: #3498db; color: white; padding: 12px 24px; 
                                      text-decoration: none; border-radius: 4px; display: inline-block;'>
                                Accept Invitation & Register
                            </a>
                        </div>
                        <p style='font-size: 0.9em; color: #666;'>
                            If you didn't expect this invitation, you can safely ignore this email.
                        </p>
                    </div>
                </body>
            </html>";
    }
}

public class SmtpSettings
{
    public string Server { get; set; }
    public int Port { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string FromEmail { get; set; }
    public string FromName { get; set; }
} 