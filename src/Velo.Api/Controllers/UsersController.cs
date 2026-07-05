using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Api.Services;

namespace Velo.Api.Controllers;

/// <summary>
/// Users API controller for viewing application users and statistics.
/// SECURITY: All endpoints require [Authorize] - validates JWT token from Azure AD B2C.
/// MULTI-TENANCY: All user data is scoped to the current org (from TenantResolutionMiddleware).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController(
    IUserTrackingService userTrackingService,
    ILogger<UsersController> logger) : ControllerBase
{
    /// <summary>
    /// Get list of users for the current organization.
    /// Returns users ordered by last access time (most recent first).
    /// </summary>
    /// <param name="skip">Number of records to skip for pagination (default: 0).</param>
    /// <param name="take">Number of records to return (default: 50, max: 500).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of application users with email and access information.</returns>
    [HttpGet("list")]
    public async Task<ActionResult<UsersListResponse>> GetUsers(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate pagination
            if (skip < 0 || take < 1 || take > 500)
                return BadRequest(new { error = "Invalid pagination parameters." });

            var users = await userTrackingService.GetUsersAsync(skip, take, true, cancellationToken);

            return Ok(new UsersListResponse(
                Users: users.Select(u => new UserDto(
                    Email: u.Email,
                    DisplayName: u.DisplayName,
                    FirstAccessAt: u.FirstAccessAt,
                    LastAccessAt: u.LastAccessAt,
                    AccessCount: u.AccessCount
                )).ToList(),
                TotalCount: users.Count
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving users list");
            return StatusCode(500, new { error = "An error occurred while retrieving users." });
        }
    }

    /// <summary>
    /// Get user statistics for the current organization.
    /// Returns total users, active users in last 24h and 7 days, and last access time.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>User statistics for the organization.</returns>
    [HttpGet("statistics")]
    public async Task<ActionResult<UserStatisticsResponse>> GetStatistics(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = await userTrackingService.GetStatisticsAsync(cancellationToken);
            return Ok(new UserStatisticsResponse(
                TotalUsers: stats.TotalUsers,
                ActiveUsersLast24Hours: stats.ActiveLast24Hours,
                ActiveUsersLast7Days: stats.ActiveLast7Days
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving user statistics");
            return StatusCode(500, new { error = "An error occurred while retrieving statistics." });
        }
    }
}

/// <summary>User data transfer object.</summary>
public record UserDto(
    string Email,
    string? DisplayName,
    DateTimeOffset FirstAccessAt,
    DateTimeOffset LastAccessAt,
    int AccessCount);

/// <summary>Response for listing users.</summary>
public record UsersListResponse(
    List<UserDto> Users,
    int TotalCount);

/// <summary>Response for user statistics.</summary>
public record UserStatisticsResponse(
    int TotalUsers,
    int ActiveUsersLast24Hours,
    int ActiveUsersLast7Days);
