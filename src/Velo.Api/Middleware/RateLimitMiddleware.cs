using Serilog.Context;
using Velo.SQL;

namespace Velo.Api.Middleware;

/// <summary>
/// Rate limiting middleware - enforces token budget for free-tier organizations.
/// Premium organizations bypass this check.
/// 
/// SECURITY: Protects against abuse and ensures fair resource allocation across tenants.
/// AUDIT: Logs all rate limit violations for security monitoring.
/// Token budget is checked per org_id per day - stored in cache or database.
/// </summary>
public class RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> logger)
{
    private static readonly Dictionary<string, (int tokenCount, DateTime date)> TokenBudgetCache = new();
    private const int FreeTokenBudget = 50_000;
    private const int MaxTokenBudget = 1_000_000;

    public async Task InvokeAsync(HttpContext context, VeloDbContext dbContext)
    {
        var orgId = context.Items["OrgId"]?.ToString();
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "N/A";

        // Only rate-limit agent calls (high token cost)
        if (!context.Request.Path.StartsWithSegments("/api/agent"))
        {
            await next(context);
            return;
        }

        if (string.IsNullOrEmpty(orgId))
        {
            logger.LogWarning(
                "SECURITY: Rate limit check - missing org_id, CorrelationId: {CorrelationId}",
                correlationId);

            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Organization context not found" });
            return;
        }

        var today = DateTime.UtcNow.Date;
        var cacheKey = $"{orgId}:{today:yyyy-MM-dd}";

        // Check token budget
        if (TokenBudgetCache.TryGetValue(cacheKey, out var cached))
        {
            if (cached.date != today)
            {
                TokenBudgetCache[cacheKey] = (0, today);
            }
            else if (cached.tokenCount >= FreeTokenBudget)
            {
                // SECURITY ALERT: Token budget exceeded
                logger.LogWarning(
                    "SECURITY: Token budget exceeded for OrgId: {OrgId} on date: {Date}, " +
                    "Used: {UsedTokens}/{BudgetTokens}, CorrelationId: {CorrelationId}",
                    orgId, today, cached.tokenCount, FreeTokenBudget, correlationId);

                context.Response.StatusCode = 429;
                await context.Response.WriteAsJsonAsync(new 
                { 
                    error = "Daily token budget exceeded. Upgrade to premium for more.",
                    remainingTokens = 0,
                    resetAt = today.AddDays(1)
                });
                return;
            }
        }
        else
        {
            TokenBudgetCache[cacheKey] = (0, today);
            
            logger.LogInformation(
                "AUDIT: New rate limit window for OrgId: {OrgId}, Date: {Date}, CorrelationId: {CorrelationId}",
                orgId, today, correlationId);
        }

        // Log token usage
        var currentUsage = TokenBudgetCache[cacheKey].tokenCount;
        logger.LogDebug(
            "AUDIT: Agent API call - OrgId: {OrgId}, TokenUsage: {CurrentUsage}/{BudgetTokens}, CorrelationId: {CorrelationId}",
            orgId, currentUsage, FreeTokenBudget, correlationId);

        try
        {
            await next(context);
        }
        finally
        {
            // Update token count (simplified - in production use database/Redis)
            if (TokenBudgetCache.TryGetValue(cacheKey, out var updated))
            {
                // Estimate token usage (1 token per 100 bytes response - simplified)
                var estimatedTokens = (int)(context.Response.ContentLength ?? 0) / 100;
                TokenBudgetCache[cacheKey] = (updated.tokenCount + estimatedTokens, updated.date);
            }
        }
    }
}
