using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    /// <param name="userId">ID/email of the user submitting feedback (from JWT).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created Feedback entity ID.</returns>
    Task<Guid> SubmitFeedbackAsync(
        string feedbackType,
        string message,
        string? projectId,
        string? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get feedback for current org with optional filtering and pagination info.
    /// </summary>
    /// <param name="feedbackType">Optional feedback type filter.</param>
    /// <param name="skip">Number of records to skip (default: 0).</param>
    /// <param name="take">Number of records to return (default: 50).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of feedback list and total count across all pages.</returns>
    Task<(List<Feedback> Feedback, int TotalCount)> GetFeedbackAsync(
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
    IConfiguration configuration,
    IFeedbackNotificationQueue notificationQueue,
    ILogger<FeedbackService> logger) : IFeedbackService
{
    public async Task<Guid> SubmitFeedbackAsync(
        string feedbackType,
        string message,
        string? projectId,
        string? userId,
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
            UserId = userId,
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
            LogSanitizer.SanitiseForLog(feedbackType),
            LogSanitizer.SanitiseForLog(db.CurrentOrgId));

        var ownerEmail = configuration["Smtp:OwnerEmail"];
        if (string.IsNullOrWhiteSpace(ownerEmail))
        {
            logger.LogWarning("Smtp:OwnerEmail is not configured. Feedback notification not queued for FeedbackId: {FeedbackId}", feedback.Id);
            return feedback.Id;
        }

        notificationQueue.Enqueue(new FeedbackNotificationWorkItem(
            OwnerEmail: ownerEmail,
            FeedbackId: feedback.Id,
            FeedbackType: feedback.FeedbackType,
            Message: feedback.Message,
            OrgId: feedback.OrgId,
            ProjectId: feedback.ProjectId,
            UserId: feedback.UserId));

        return feedback.Id;
    }

    public async Task<(List<Feedback> Feedback, int TotalCount)> GetFeedbackAsync(
        string? feedbackType = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var query = db.Feedback.AsQueryable();

        if (!string.IsNullOrEmpty(feedbackType))
            query = query.Where(f => f.FeedbackType == feedbackType);

        // Get total count before pagination
        var totalCount = await query.CountAsync(cancellationToken);

        // Get paginated results
        var feedback = await query
            .OrderByDescending(f => f.CreatedDate)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (feedback, totalCount);
    }

}
