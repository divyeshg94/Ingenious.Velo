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
    /// True when SMTP is fully configured (host/username/from address). Callers that must
    /// not silently no-op — e.g. a campaign that permanently marks a recipient as
    /// "contacted" — should check this before sending rather than relying on a successful
    /// return from a send call that may have skipped sending entirely.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Send a feedback notification email asynchronously.
    /// </summary>
    /// <param name="toEmail">Recipient email address (Velo owner).</param>
    /// <param name="feedbackType">Type of feedback (Bug, FeatureRequest, etc.).</param>
    /// <param name="message">Feedback message from user.</param>
    /// <param name="orgId">Organization ID for context.</param>
    /// <param name="projectId">Optional project ID for context.</param>
    /// <param name="userId">Email of the user who submitted feedback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendFeedbackNotificationAsync(
        string toEmail,
        string feedbackType,
        string message,
        string orgId,
        string? projectId,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a re-engagement email to an org's admin contact that never activated Velo
    /// (registered but never synced a pipeline). Always includes an unsubscribe link —
    /// required by CAN-SPAM/GDPR and by <see cref="SendReEngagementEmailAsync"/>'s callers.
    /// </summary>
    /// <param name="toEmail">Admin contact email captured at registration.</param>
    /// <param name="orgDisplayName">Org display name for personalization.</param>
    /// <param name="unsubscribeUrl">One-click, token-verified opt-out link.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendReEngagementEmailAsync(
        string toEmail,
        string orgDisplayName,
        string unsubscribeUrl,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Gmail SMTP-based email service implementation.
/// Requires Gmail configuration in settings: Smtp:Host, Smtp:Port, Smtp:Username, Smtp:Password, Smtp:FromEmail
/// </summary>
public class GmailEmailService(IConfiguration configuration, ILogger<GmailEmailService> logger) : IEmailService
{
    public bool IsConfigured =>
        !string.IsNullOrEmpty(configuration["Smtp:Host"])
        && !string.IsNullOrEmpty(configuration["Smtp:Username"])
        && !string.IsNullOrEmpty(configuration["Smtp:FromEmail"]);

    public async Task SendFeedbackNotificationAsync(
        string toEmail,
        string feedbackType,
        string message,
        string orgId,
        string? projectId,
        string? userId = null,
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
            var subject = $"Velo Feedback: {feedbackType} from {orgId}";
            var body = BuildEmailBody(feedbackType, message, orgId, projectId, userId);

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
                    // cs:suppress Exposure of private information - toEmail is config-controlled Smtp:OwnerEmail, not user input
                    logger.LogInformation("Feedback notification sent to {RecipientEmail}", LogSanitizer.SanitiseForLog(toEmail));
                }
            }
        }
        catch (Exception ex)
        {
            // cs:suppress Exposure of private information - toEmail is config-controlled Smtp:OwnerEmail, not user input
            logger.LogError(ex, "Failed to send feedback notification email. Recipient: {Email}", LogSanitizer.SanitiseForLog(toEmail));
            throw;
        }
    }

    public async Task SendReEngagementEmailAsync(
        string toEmail,
        string orgDisplayName,
        string unsubscribeUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var smtpHost = configuration["Smtp:Host"];
            var smtpPort = configuration.GetValue<int>("Smtp:Port", 587);
            var smtpUsername = configuration["Smtp:Username"];
            var smtpPassword = configuration["Smtp:Password"];
            var fromEmail = configuration["Smtp:FromEmail"];

            if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(smtpUsername) || string.IsNullOrEmpty(fromEmail))
            {
                logger.LogWarning("Gmail SMTP configuration incomplete. Re-engagement email not sent.");
                return;
            }

            if (string.IsNullOrWhiteSpace(toEmail))
            {
                logger.LogWarning("Recipient email is empty. Re-engagement email not sent.");
                return;
            }

            var subject = $"Get more out of Velo, {HtmlEncode(orgDisplayName)}";
            var body = BuildReEngagementEmailBody(orgDisplayName, unsubscribeUrl);

            using var client = new SmtpClient(smtpHost, smtpPort);
            client.EnableSsl = true;
            client.Credentials = new NetworkCredential(smtpUsername, smtpPassword);
            client.Timeout = 10000;

            using var mailMessage = new MailMessage(fromEmail, toEmail);
            mailMessage.Subject = subject;
            mailMessage.Body = body;
            mailMessage.IsBodyHtml = true;

            await client.SendMailAsync(mailMessage, cancellationToken);
            // cs:suppress Exposure of private information - toEmail is the org's own registered admin contact
            logger.LogInformation("Re-engagement email sent to {RecipientEmail}", LogSanitizer.SanitiseForLog(toEmail));
        }
        catch (Exception ex)
        {
            // cs:suppress Exposure of private information - toEmail is the org's own registered admin contact
            logger.LogError(ex, "Failed to send re-engagement email. Recipient: {Email}", LogSanitizer.SanitiseForLog(toEmail));
            throw;
        }
    }

    private static string BuildReEngagementEmailBody(string orgDisplayName, string unsubscribeUrl) => $@"
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; color: #333; }}
        .container {{ max-width: 600px; margin: 20px auto; }}
        .header {{ background-color: #0078d4; color: white; padding: 20px; border-radius: 5px 5px 0 0; }}
        .content {{ background-color: #f5f5f5; padding: 20px; border-radius: 0 0 5px 5px; }}
        .cta {{ display: inline-block; margin-top: 12px; padding: 10px 20px; background-color: #0078d4; color: white; text-decoration: none; border-radius: 4px; }}
        .footer {{ margin-top: 24px; font-size: 11px; color: #777; }}
        .footer a {{ color: #777; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h2>Still there, {HtmlEncode(orgDisplayName)}?</h2>
        </div>
        <div class=""content"">
            <p>You connected {HtmlEncode(orgDisplayName)} to Velo, but we haven't seen any pipeline data yet.</p>
            <p>Two minutes gets you: automatic DORA metrics from your Azure DevOps pipelines, an AI agent that answers questions about delivery health, and a link you can share with leadership.</p>
            <p><a class=""cta"" href=""https://marketplace.visualstudio.com/items?itemName=IngeniousLabs.velo"">Finish setup</a></p>
            <div class=""footer"">
                <p>You're receiving this because this address was given as the admin contact when {HtmlEncode(orgDisplayName)} connected to Velo.
                <a href=""{HtmlEncode(unsubscribeUrl)}"">Unsubscribe from these emails</a>.</p>
            </div>
        </div>
    </div>
</body>
</html>";

    private static string BuildEmailBody(string feedbackType, string message, string orgId, string? projectId, string? userId = null)
    {
        var projectInfo = !string.IsNullOrEmpty(projectId) ? $"<p><strong>Project:</strong> {HtmlEncode(projectId)}</p>" : "";
        var userInfo = !string.IsNullOrEmpty(userId) ? $"<div class=\"field\"><span class=\"label\">Submitted by:</span> {HtmlEncode(userId)}</div>" : "";

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
            {userInfo}
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
