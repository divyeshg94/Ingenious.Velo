using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Api.Services;
using Velo.Shared.Models;

namespace Velo.Api.Controllers;

/// <summary>
/// Feedback API controller for user feedback submission and retrieval.
/// SECURITY: All endpoints require [Authorize] - validates JWT token from Azure AD B2C.
/// MULTI-TENANCY: All feedback is scoped to the current org (from TenantResolutionMiddleware).
/// ROW-LEVEL SECURITY: SQL Server RLS policies enforce org_id scoping at the database layer.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FeedbackController(
    IFeedbackService feedbackService,
    ILogger<FeedbackController> logger) : ControllerBase
{
    /// <summary>
    /// Submit user feedback.
    /// The feedback is associated with the current org automatically.
    /// If an organization notification email is configured in settings,
    /// an email notification will be sent asynchronously.
    /// </summary>
    /// <param name="request">Feedback submission request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ID of the created feedback record.</returns>
    [HttpPost("submit")]
    public async Task<ActionResult<FeedbackSubmitResponse>> SubmitFeedback(
        [FromBody] FeedbackSubmitRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var feedbackId = await feedbackService.SubmitFeedbackAsync(
                request.FeedbackType,
                request.Message,
                request.ProjectId,
                User.FindFirst("emails")?.Value ??
                User.FindFirst("email")?.Value ??
                User.FindFirst("preferred_username")?.Value,
                cancellationToken);

            return Ok(new FeedbackSubmitResponse(
                FeedbackId: feedbackId,
                Message: "Feedback submitted successfully. Thank you!"
            ));
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning("Invalid feedback submission: {Error}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error submitting feedback");
            return StatusCode(500, new { error = "An error occurred while submitting feedback." });
        }
    }

    /// <summary>
    /// Get feedback for the current organization.
    /// Supports filtering by feedback type and pagination.
    /// </summary>
    /// <param name="feedbackType">Optional feedback type filter.</param>
    /// <param name="skip">Number of records to skip (default: 0).</param>
    /// <param name="take">Number of records to return (default: 50, max: 500).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of feedback records.</returns>
    [HttpGet("list")]
    public async Task<ActionResult<FeedbackListResponse>> GetFeedback(
        [FromQuery] string? feedbackType = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate pagination
            if (skip < 0 || take < 1 || take > 500)
                return BadRequest(new { error = "Invalid pagination parameters." });

            var feedbackResult = await feedbackService.GetFeedbackAsync(
                feedbackType,
                skip,
                take,
                cancellationToken);

            return Ok(new FeedbackListResponse(
                Feedback: feedbackResult.Feedback.Select(f => new FeedbackDto(
                    Id: f.Id,
                    FeedbackType: f.FeedbackType,
                    Message: f.Message,
                    ProjectId: f.ProjectId,
                    CreatedAt: f.CreatedDate,
                    IsReviewed: f.IsReviewed
                )).ToList(),
                TotalCount: feedbackResult.TotalCount
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving feedback");
            return StatusCode(500, new { error = "An error occurred while retrieving feedback." });
        }
    }
}

/// <summary>Request for submitting feedback.</summary>
public record FeedbackSubmitRequest(
    string FeedbackType,
    string Message,
    string? ProjectId = null);

/// <summary>Response after submitting feedback.</summary>
public record FeedbackSubmitResponse(
    Guid FeedbackId,
    string Message);

/// <summary>Feedback data transfer object.</summary>
public record FeedbackDto(
    Guid Id,
    string FeedbackType,
    string Message,
    string? ProjectId,
    DateTimeOffset CreatedAt,
    bool IsReviewed);

/// <summary>Response for listing feedback.</summary>
public record FeedbackListResponse(
    List<FeedbackDto> Feedback,
    int TotalCount);
