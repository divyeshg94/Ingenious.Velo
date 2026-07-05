using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Velo.Api.Logging;
using Velo.SQL;
using Velo.SQL.Models;

namespace Velo.Api.Services;

/// <summary>
/// Service for tracking application user access and email information.
/// </summary>
public interface IUserTrackingService
{
    /// <summary>
    /// Record or update user access. Called on every request to track active users.
    /// Asynchronous fire-and-forget to avoid blocking request pipeline.
    /// </summary>
    /// <param name="email">User's email address from Azure AD token.</param>
    /// <param name="displayName">User's display name (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user record (created or updated).</returns>
    Task<ApplicationUser> TrackUserAccessAsync(
        string email,
        string? displayName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get list of users for the current organization.
    /// </summary>
    /// <param name="skip">Number of records to skip for pagination.</param>
    /// <param name="take">Number of records to return (max 500).</param>
    /// <param name="orderByLastAccess">Order by last access time descending if true, else by email.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of application users in the organization.</returns>
    Task<List<ApplicationUser>> GetUsersAsync(
        int skip = 0,
        int take = 50,
        bool orderByLastAccess = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user statistics for the current organization.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Statistics including total users and last 24h active users.</returns>
    Task<UserStatisticsDto> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// User statistics data transfer object.
/// </summary>
public record UserStatisticsDto(
    int TotalUsers,
    int ActiveLast24Hours,
    int ActiveLast7Days,
    DateTimeOffset? LastUserAccess);

/// <summary>
/// Implementation of user tracking service.
/// </summary>
public class UserTrackingService(
    VeloDbContext db,
    ILogger<UserTrackingService> logger) : IUserTrackingService
{
    public async Task<ApplicationUser> TrackUserAccessAsync(
        string email,
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));

        var orgId = db.CurrentOrgId!;
        var now = DateTimeOffset.UtcNow;

        try
        {
            // Try to find existing user
            var user = await db.ApplicationUsers
                .FirstOrDefaultAsync(u => u.OrgId == orgId && u.Email == email, cancellationToken);

            if (user != null)
            {
                // Update last access and increment counter
                user.LastAccessAt = now;
                user.AccessCount++;
                if (!string.IsNullOrEmpty(displayName) && string.IsNullOrEmpty(user.DisplayName))
                    user.DisplayName = displayName;
            }
            else
            {
                // Create new user record
                user = new ApplicationUser
                {
                    OrgId = orgId,
                    Email = email,
                    DisplayName = displayName,
                    FirstAccessAt = now,
                    LastAccessAt = now,
                    AccessCount = 1
                };
                db.ApplicationUsers.Add(user);
            }

            await db.SaveChangesAsync(cancellationToken);
            return user;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error tracking user access for email: {Email}, OrgId: {OrgId}",
                LogSanitizer.SanitiseForLog(email), LogSanitizer.SanitiseForLog(orgId));
            throw;
        }
    }

    public async Task<List<ApplicationUser>> GetUsersAsync(
        int skip = 0,
        int take = 50,
        bool orderByLastAccess = true,
        CancellationToken cancellationToken = default)
    {
        // Validate pagination
        if (skip < 0 || take < 1 || take > 500)
            throw new ArgumentException("Invalid pagination parameters.");

        var query = db.ApplicationUsers.AsQueryable();

        if (orderByLastAccess)
            query = query.OrderByDescending(u => u.LastAccessAt);
        else
            query = query.OrderBy(u => u.Email);

        return await query
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserStatisticsDto> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var last24Hours = now.AddHours(-24);
        var last7Days = now.AddDays(-7);

        var totalUsers = await db.ApplicationUsers.CountAsync(cancellationToken);
        var activeLast24Hours = await db.ApplicationUsers
            .CountAsync(u => u.LastAccessAt >= last24Hours, cancellationToken);
        var activeLast7Days = await db.ApplicationUsers
            .CountAsync(u => u.LastAccessAt >= last7Days, cancellationToken);
        var lastUserAccess = await db.ApplicationUsers
            .OrderByDescending(u => u.LastAccessAt)
            .Select(u => u.LastAccessAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new UserStatisticsDto(
            TotalUsers: totalUsers,
            ActiveLast24Hours: activeLast24Hours,
            ActiveLast7Days: activeLast7Days,
            LastUserAccess: lastUserAccess);
    }
}
