using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Velo.Api.Logging;
using Velo.SQL;
using Velo.SQL.Models;

namespace Velo.Api.Services;

/// <summary>
/// Service for handling user feedback submission and email notifications.
/// </summary>
public interface IFeedbackService
{
    /// <summary>
    /// Submit feedback and send notification email if configured.
    /// </summary>
    /// <param name="feedbackType">Type of feedback (Bug, FeatureRequest, MetricConcern, PerformanceIssue).</param>
    /// <param name="message">User feedback message.</param>
    /// <param name="projectId">Optional project ID for context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created Feedback entity ID.</returns>
    Task<Guid> SubmitFeedbackAsync(
        string feedbackType,
        string message,
        string? projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get feedback for current org with optional filtering.
    /// </summary>
    Task<List<Feedback>> GetFeedbackAsync(
        string? feedbackType = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of feedback service with email notification.
/// </summary>
public class FeedbackService(
    VeloDbContext db,
    IEmailService emailService,
    ILogger<FeedbackService> logger) : IFeedbackService
{
    public async Task<Guid> SubmitFeedbackAsync(
        string feedbackType,
        string message,
        string? projectId,
        CancellationToken cancellationToken = default)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(feedbackType))
            throw new ArgumentException("Feedback type is required.", nameof(feedbackType));

        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Feedback message is required.", nameof(message));

        if (message.Length > 2000)
            throw new ArgumentException("Feedback message cannot exceed 2000 characters.", nameof(message));

        // Validate feedback type
        var validTypes = new[] { "Bug", "FeatureRequest", "MetricConcern", "PerformanceIssue" };
        if (!validTypes.Contains(feedbackType))
            throw new ArgumentException($"Invalid feedback type. Must be one of: {string.Join(", ", validTypes)}", nameof(feedbackType));

        // Create feedback record
        var feedback = new Feedback
        {
            OrgId = db.CurrentOrgId!,
            FeedbackType = feedbackType,
            Message = message,
            ProjectId = projectId,
            IsReviewed = false
        };

        db.Feedback.Add(feedback);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Feedback submitted - FeedbackId: {FeedbackId}, Type: {Type}, OrgId: {OrgId}",
            feedback.Id,
            feedbackType,
            LogSanitizer.SanitiseForLog(db.CurrentOrgId));

        // Send notification email asynchronously (fire and forget)
        _ = SendNotificationEmailAsync(feedback, cancellationToken);

        return feedback.Id;
    }

    public async Task<List<Feedback>> GetFeedbackAsync(
        string? feedbackType = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var query = db.Feedback.AsQueryable();

        if (!string.IsNullOrEmpty(feedbackType))
            query = query.Where(f => f.FeedbackType == feedbackType);

        return await query
            .OrderByDescending(f => f.CreatedDate)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    private async Task SendNotificationEmailAsync(Feedback feedback, CancellationToken cancellationToken)
    {
        try
        {
            // Get org settings to find notification email
            var settings = await db.OrganizationSettings
                .Where(s => s.OrgId == feedback.OrgId)
                .FirstOrDefaultAsync(cancellationToken);

            if (settings?.FeedbackNotificationEmail == null)
            {
                logger.LogInformation(
                    "No feedback notification email configured for OrgId: {OrgId}",
                    LogSanitizer.SanitiseForLog(feedback.OrgId));
                return;
            }

            await emailService.SendFeedbackNotificationAsync(
                settings.FeedbackNotificationEmail,
                feedback.FeedbackType,
                feedback.Message,
                feedback.OrgId,
                feedback.ProjectId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to send feedback notification for FeedbackId: {FeedbackId}",
                feedback.Id);
        }
    }
}
