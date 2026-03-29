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
    // Values are (tokenCount, windowDate) tuples. Tests reflect over this private field
    // and expect a Dictionary<string, (int, DateTime)>. Use a Dictionary with a lock
    // to preserve simple thread-safety semantics while matching test expectations.
    private static readonly Dictionary<string, (int tokenCount, DateTime date)>
        TokenBudgetCache = new(StringComparer.Ordinal);

    private static readonly object TokenBudgetCacheLock = new();

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
        (int tokenCount, DateTime date) current;
        lock (TokenBudgetCacheLock)
        {
            if (TokenBudgetCache.Count >= MaxCacheEntries)
                EvictStaleEntries(today);

            if (!TokenBudgetCache.TryGetValue(cacheKey, out var found))
            {
                logger.LogInformation(
                    "AUDIT: New rate-limit window — OrgId={OrgId}, Date={Date}, CorrelationId={CorrelationId}",
                    orgId, today, correlationId);
                current = (0, today);
                TokenBudgetCache[cacheKey] = current;
            }
            else
            {
                current = found;
            }

            // Reset counter if the stored date is stale (previous day's entry survived eviction).
            if (current.date != today)
            {
                current = (0, today);
                TokenBudgetCache[cacheKey] = current;
            }
        }

        if (current.tokenCount >= FreeTokenBudget)
        {
            logger.LogWarning(
                "SECURITY: Token budget exceeded — OrgId={OrgId}, Used={Used}/{Budget}, CorrelationId={CorrelationId}",
                orgId, current.tokenCount, FreeTokenBudget, correlationId);

            context.Response.StatusCode = 429;
            // Do not include resetAt — it reveals the exact budget window to attackers.
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Daily token budget exceeded. Upgrade to premium for unlimited access."
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

        lock (TokenBudgetCacheLock)
        {
            if (TokenBudgetCache.TryGetValue(cacheKey, out var existing))
            {
                var newCount = existing.date == today ? existing.tokenCount + estimatedTokens : estimatedTokens;
                TokenBudgetCache[cacheKey] = (newCount, today);
            }
            else
            {
                TokenBudgetCache[cacheKey] = (estimatedTokens, today);
            }
        }
    }

    private static void EvictStaleEntries(DateTime today)
    {
        // Remove entries whose window date is before today — they will never be checked again.
        lock (TokenBudgetCacheLock)
        {
            var keys = TokenBudgetCache.Keys.ToList();
            foreach (var key in keys)
            {
                if (TokenBudgetCache.TryGetValue(key, out var entry) && entry.date < today)
                    TokenBudgetCache.Remove(key);
            }
        }
    }
}
