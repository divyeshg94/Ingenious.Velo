using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Velo.Api.Logging;
using Velo.SQL;
using Velo.SQL.Models;

namespace Velo.Api.Services;

/// <summary>
/// Service for managing organization settings.
/// </summary>
public interface IOrganizationSettingsService
{
    /// <summary>
    /// Get settings for the current organization.
    /// </summary>
    Task<OrganizationSettings> GetSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update feedback notification email for the current organization.
    /// </summary>
    Task<OrganizationSettings> UpdateFeedbackEmailAsync(
        string? feedbackNotificationEmail,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of organization settings service.
/// </summary>
public class OrganizationSettingsService(
    VeloDbContext db,
    ILogger<OrganizationSettingsService> logger) : IOrganizationSettingsService
{
    public async Task<OrganizationSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var orgId = db.CurrentOrgId!;

        var settings = await db.OrganizationSettings
            .FirstOrDefaultAsync(s => s.OrgId == orgId, cancellationToken);

        // Create default settings if they don't exist
        if (settings == null)
        {
            settings = new OrganizationSettings { OrgId = orgId };
            db.OrganizationSettings.Add(settings);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Created default organization settings for OrgId: {OrgId}", LogSanitizer.SanitiseForLog(orgId));
        }

        return settings;
    }

    public async Task<OrganizationSettings> UpdateFeedbackEmailAsync(
        string? feedbackNotificationEmail,
        CancellationToken cancellationToken = default)
    {
        var orgId = db.CurrentOrgId!;

        // Validate email format if provided
        if (!string.IsNullOrWhiteSpace(feedbackNotificationEmail))
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(feedbackNotificationEmail);
                if (addr.Address != feedbackNotificationEmail)
                    throw new ArgumentException("Invalid email address.", nameof(feedbackNotificationEmail));
            }
            catch
            {
                throw new ArgumentException("Invalid email address format.", nameof(feedbackNotificationEmail));
            }
        }

        var settings = await db.OrganizationSettings
            .FirstOrDefaultAsync(s => s.OrgId == orgId, cancellationToken);

        if (settings == null)
        {
            settings = new OrganizationSettings { OrgId = orgId };
            db.OrganizationSettings.Add(settings);
        }

        settings.FeedbackNotificationEmail = feedbackNotificationEmail;
        settings.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Updated feedback notification email for OrgId: {OrgId}",
            LogSanitizer.SanitiseForLog(orgId));

        return settings;
    }
}
