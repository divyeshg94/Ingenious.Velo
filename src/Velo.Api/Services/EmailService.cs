using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using Velo.Api.Logging;

namespace Velo.Api.Services;

/// <summary>
/// Email service for sending feedback notifications using Gmail SMTP.
/// Configuration is loaded from IConfiguration (app secrets or Key Vault).
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Send a feedback notification email asynchronously.
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="feedbackType">Type of feedback (Bug, FeatureRequest, etc.).</param>
    /// <param name="message">Feedback message from user.</param>
    /// <param name="orgId">Organization ID for context.</param>
    /// <param name="projectId">Optional project ID for context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendFeedbackNotificationAsync(
        string toEmail,
        string feedbackType,
        string message,
        string orgId,
        string? projectId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Gmail SMTP-based email service implementation.
/// Requires Gmail configuration in settings: Smtp:Host, Smtp:Port, Smtp:Username, Smtp:Password, Smtp:FromEmail
/// </summary>
public class GmailEmailService(IConfiguration configuration, ILogger<GmailEmailService> logger) : IEmailService
{
    public async Task SendFeedbackNotificationAsync(
        string toEmail,
        string feedbackType,
        string message,
        string orgId,
        string? projectId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Load Gmail SMTP configuration
            var smtpHost = configuration["Smtp:Host"];
            var smtpPort = configuration.GetValue<int>("Smtp:Port", 587);
            var smtpUsername = configuration["Smtp:Username"];
            var smtpPassword = configuration["Smtp:Password"];
            var fromEmail = configuration["Smtp:FromEmail"];

            if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(smtpUsername) || string.IsNullOrEmpty(fromEmail))
            {
                logger.LogWarning("Gmail SMTP configuration incomplete. Feedback notification not sent.");
                return;
            }

            // Validate recipient email
            if (string.IsNullOrWhiteSpace(toEmail))
            {
                logger.LogWarning("Recipient email is empty. Feedback notification not sent.");
                return;
            }

            // Build email body
            var subject = $"Velo Feedback: {feedbackType}";
            var body = BuildEmailBody(feedbackType, message, orgId, projectId);

            using (var client = new SmtpClient(smtpHost, smtpPort))
            {
                client.EnableSsl = true;
                client.Credentials = new NetworkCredential(smtpUsername, smtpPassword);
                client.Timeout = 10000;

                using (var mailMessage = new MailMessage(fromEmail, toEmail))
                {
                    mailMessage.Subject = subject;
                    mailMessage.Body = body;
                    mailMessage.IsBodyHtml = true;

                    await client.SendMailAsync(mailMessage, cancellationToken);
                    // cs:suppress Exposure of private information - toEmail is config-controlled, not user input
                    logger.LogInformation("Feedback notification sent to {RecipientEmail}", LogSanitizer.SanitiseForLog(toEmail));
                }
            }
        }
        catch (Exception ex)
        {
            // cs:suppress Exposure of private information - toEmail is config-controlled, not user input
            logger.LogError(ex, "Failed to send feedback notification email. Recipient: {Email}", LogSanitizer.SanitiseForLog(toEmail));
            throw;
        }
    }

    private static string BuildEmailBody(string feedbackType, string message, string orgId, string? projectId)
    {
        var projectInfo = !string.IsNullOrEmpty(projectId) ? $"<p><strong>Project:</strong> {HtmlEncode(projectId)}</p>" : "";

        return $@"
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; color: #333; }}
        .container {{ max-width: 600px; margin: 20px auto; }}
        .header {{ background-color: #0078d4; color: white; padding: 20px; border-radius: 5px 5px 0 0; }}
        .content {{ background-color: #f5f5f5; padding: 20px; border-radius: 0 0 5px 5px; }}
        .field {{ margin-bottom: 15px; }}
        .label {{ font-weight: bold; color: #0078d4; }}
        .message {{ background-color: white; padding: 15px; border-left: 4px solid #0078d4; margin-top: 10px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h2>New Velo Feedback Received</h2>
        </div>
        <div class=""content"">
            <div class=""field"">
                <span class=""label"">Type:</span> {HtmlEncode(feedbackType)}
            </div>
            <div class=""field"">
                <span class=""label"">Organization:</span> {HtmlEncode(orgId)}
            </div>
            {projectInfo}
            <div class=""field"">
                <span class=""label"">Message:</span>
                <div class=""message"">
                    {HtmlEncode(message)}
                </div>
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private static string HtmlEncode(string text) => WebUtility.HtmlEncode(text);
}
