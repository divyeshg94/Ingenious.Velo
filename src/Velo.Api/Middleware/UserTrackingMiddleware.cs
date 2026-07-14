using Microsoft.Extensions.Logging;
using Velo.Api.Helpers;
using Velo.Api.Logging;
using Velo.Api.Services;

namespace Velo.Api.Middleware;

/// <summary>
/// Middleware that tracks application user access asynchronously.
/// Runs after TenantResolutionMiddleware and Authorization, so org_id and user context are available.
/// Fire-and-forget pattern: logs errors but does not block the request if tracking fails.
/// </summary>
public class UserTrackingMiddleware(RequestDelegate next, ILogger<UserTrackingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IUserTrackingService userTrackingService)
    {
        // Only track authenticated users in API endpoints
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var userIdentifier = UserIdentityResolver.ResolveUserIdentifier(context.User);

            // Extract display name if available
            var displayName = UserIdentityResolver.ResolveDisplayName(context.User);

            if (!string.IsNullOrEmpty(userIdentifier))
            {
                // Start tracking task before next() so service scope stays alive
                var trackingTask = TrackUserInBackgroundAsync(userTrackingService, userIdentifier, displayName);
                try
                {
                    await next(context);
                }
                finally
                {
                    // Await tracking after next() to keep scope alive and ensure completion
                    await trackingTask;
                }
            }
            else
            {
                await next(context);
            }
        }
        else
        {
            await next(context);
        }
    }

    private async Task TrackUserInBackgroundAsync(
        IUserTrackingService userTrackingService,
        string userIdentifier,
        string? displayName)
    {
        try
        {
            // Use a timeout to prevent hanging if the database is slow
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await userTrackingService.TrackUserAccessAsync(userIdentifier, displayName, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // cs:suppress Exposure of private information - identifier is sanitized via LogSanitizer
            logger.LogDebug("User tracking timed out for user identifier: {UserIdentifier}", LogSanitizer.SanitiseForLog(userIdentifier));
        }
        catch (Exception ex)
        {
            // cs:suppress Exposure of private information - identifier is sanitized via LogSanitizer
            logger.LogWarning(ex, "Failed to track user access for user identifier: {UserIdentifier}", LogSanitizer.SanitiseForLog(userIdentifier));
        }
    }
}
