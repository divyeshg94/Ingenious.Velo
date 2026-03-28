using System.Collections.Concurrent;
using Serilog.Context;
using Velo.SQL;

namespace Velo.Api.Middleware;

/// <summary>
/// Per-org daily token budget enforcement for AI agent calls.
///
/// SECURITY: Uses a ConcurrentDictionary so concurrent requests across threads cannot
/// race-condition their way past the budget check.
///
/// KNOWN LIMITATION: The cache is in-process. With multiple Container App instances each
/// instance has an independent budget window — effective budget is N × FreeTokenBudget where
/// N is the replica count. A Redis / Azure Cache distributed counter is the correct long-term
/// fix; tracked in the backlog. For Phase 1 (single replica) this is sufficient.
///
/// MEMORY SAFETY: The cache is capped at MaxCacheEntries entries. When the cap is reached old
/// entries are evicted before adding new ones to prevent unbounded memory growth from stale keys.
/// </summary>
public class RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> logger)
{
    // Thread-safe; values are (tokenCount, windowDate) tuples replaced atomically.
    private static readonly ConcurrentDictionary<string, (int tokenCount, DateTime date)>
        TokenBudgetCache = new(StringComparer.Ordinal);

    private const int FreeTokenBudget  = 50_000;
    private const int MaxCacheEntries  = 10_000;   // ~10 k orgs × 1 entry each

    public async Task InvokeAsync(HttpContext context, VeloDbContext dbContext)
    {
        var orgId         = context.Items["OrgId"]?.ToString();
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "N/A";

        // Only rate-limit AI agent calls (high token cost); all other endpoints pass through.
        if (!context.Request.Path.StartsWithSegments("/api/agent"))
        {
            await next(context);
            return;
        }

        if (string.IsNullOrEmpty(orgId))
        {
            logger.LogWarning(
                "SECURITY: Rate limit check — missing org_id. CorrelationId={CorrelationId}",
                correlationId);

            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Organization context not found" });
            return;
        }

        var today    = DateTime.UtcNow.Date;
        var cacheKey = $"{orgId}:{today:yyyy-MM-dd}";

        // Evict stale entries if the cache is growing too large.
        if (TokenBudgetCache.Count >= MaxCacheEntries)
            EvictStaleEntries(today);

        // GetOrAdd is atomic for the initial entry; subsequent updates use AddOrUpdate.
        var current = TokenBudgetCache.GetOrAdd(cacheKey, _ =>
        {
            logger.LogInformation(
                "AUDIT: New rate-limit window — OrgId={OrgId}, Date={Date}, CorrelationId={CorrelationId}",
                orgId, today, correlationId);
            return (0, today);
        });

        // Reset counter if the stored date is stale (previous day's entry survived eviction).
        if (current.date != today)
        {
            current = (0, today);
            TokenBudgetCache[cacheKey] = current;
        }

        if (current.tokenCount >= FreeTokenBudget)
        {
            logger.LogWarning(
                "SECURITY: Token budget exceeded — OrgId={OrgId}, Used={Used}/{Budget}, CorrelationId={CorrelationId}",
                orgId, current.tokenCount, FreeTokenBudget, correlationId);

            context.Response.StatusCode = 429;
            await context.Response.WriteAsJsonAsync(new
            {
                error          = "Daily token budget exceeded. Upgrade to premium for unlimited access.",
                remainingTokens = 0,
                resetAt        = today.AddDays(1)
            });
            return;
        }

        logger.LogDebug(
            "AUDIT: Agent call — OrgId={OrgId}, Usage={Used}/{Budget}, CorrelationId={CorrelationId}",
            orgId, current.tokenCount, FreeTokenBudget, correlationId);

        await next(context);

        // Update token count after the response is produced.
        // Estimate: 1 token ≈ 4 bytes of response (conservative rough estimate).
        // TODO: Replace with actual token count returned by the Foundry SDK in AgentService.
        var estimatedTokens = (int)Math.Max(1, (context.Response.ContentLength ?? 500) / 4);

        TokenBudgetCache.AddOrUpdate(
            cacheKey,
            addValue: (estimatedTokens, today),
            updateValueFactory: (_, existing) =>
                (existing.date == today ? existing.tokenCount + estimatedTokens : estimatedTokens, today));
    }

    private static void EvictStaleEntries(DateTime today)
    {
        // Remove entries whose window date is before today — they will never be checked again.
        foreach (var key in TokenBudgetCache.Keys)
        {
            if (TokenBudgetCache.TryGetValue(key, out var entry) && entry.date < today)
                TokenBudgetCache.TryRemove(key, out _);
        }
    }
}
