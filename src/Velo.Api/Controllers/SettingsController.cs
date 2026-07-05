using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Api.Services;

namespace Velo.Api.Controllers;

/// <summary>
/// Organization Settings API controller for managing org-level configuration.
/// SECURITY: All endpoints require [Authorize] - validates JWT token from Azure AD B2C.
/// MULTI-TENANCY: Settings are scoped to the current org (from TenantResolutionMiddleware).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SettingsController(
    IOrganizationSettingsService settingsService,
    ILogger<SettingsController> logger) : ControllerBase
{
    /// <summary>
    /// Get organization settings for the current org.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Organization settings.</returns>
    [HttpGet]
    public async Task<ActionResult<OrganizationSettingsDto>> GetSettings(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await settingsService.GetSettingsAsync(cancellationToken);

            return Ok(new OrganizationSettingsDto(
                FeedbackNotificationEmail: settings.FeedbackNotificationEmail
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving organization settings");
            return StatusCode(500, new { error = "An error occurred while retrieving settings." });
        }
    }

    /// <summary>
    /// Update the feedback notification email for the current org.
    /// </summary>
    /// <param name="request">Update request with new email address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated organization settings.</returns>
    [HttpPut("feedback-email")]
    public async Task<ActionResult<OrganizationSettingsDto>> UpdateFeedbackEmail(
        [FromBody] UpdateFeedbackEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var settings = await settingsService.UpdateFeedbackEmailAsync(
                request.FeedbackNotificationEmail,
                cancellationToken);

            return Ok(new OrganizationSettingsDto(
                FeedbackNotificationEmail: settings.FeedbackNotificationEmail
            ));
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning("Invalid settings update: {Error}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating organization settings");
            return StatusCode(500, new { error = "An error occurred while updating settings." });
        }
    }
}

/// <summary>Request for updating feedback notification email.</summary>
public record UpdateFeedbackEmailRequest(
    string? FeedbackNotificationEmail);

/// <summary>Organization settings data transfer object.</summary>
public record OrganizationSettingsDto(
    string? FeedbackNotificationEmail);
