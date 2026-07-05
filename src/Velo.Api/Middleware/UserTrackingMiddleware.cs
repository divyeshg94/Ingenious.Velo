using System.Security.Claims;
using Microsoft.Extensions.Logging;
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
            // Extract email from JWT — Azure AD B2C uses 'emails' claim (array) or falls back to 'oid'
            var email = context.User.FindFirst("emails")?.Value
                ?? context.User.FindFirst("email")?.Value
                ?? context.User.FindFirst("preferred_username")?.Value
                ?? context.User.FindFirst("oid")?.Value;

            // Extract display name if available
            var displayName = context.User.FindFirst("name")?.Value
                ?? context.User.FindFirst("given_name")?.Value;

            if (!string.IsNullOrEmpty(email))
            {
                // Fire-and-forget: track user access in background to avoid blocking the request
                _ = TrackUserInBackgroundAsync(userTrackingService, email, displayName);
            }
        }

        await next(context);
    }

    private async Task TrackUserInBackgroundAsync(
        IUserTrackingService userTrackingService,
        string email,
        string? displayName)
    {
        try
        {
            // Use a timeout to prevent hanging if the database is slow
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await userTrackingService.TrackUserAccessAsync(email, displayName, cts.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("User tracking timed out for email: {Email}", LogSanitizer.SanitiseForLog(email));
        }
        catch (Exception ex)
        {
            // Log but do not rethrow — user tracking failures must never break the main request
            logger.LogWarning(ex, "Failed to track user access for email: {Email}", LogSanitizer.SanitiseForLog(email));
        }
    }
}
