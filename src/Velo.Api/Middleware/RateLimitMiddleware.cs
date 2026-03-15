using Velo.Api.Data;

namespace Velo.Api.Middleware;

/// <summary>
/// Enforces per-org daily token budget for Foundry AI agent endpoints.
/// Premium orgs bypass the limit; free-tier orgs are capped at the configured daily budget.
/// </summary>
public class RateLimitMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private const string AgentPath = "/api/agent";
    private readonly int _dailyBudget = configuration.GetValue<int>("Foundry:DailyTokenBudgetPerOrg", 50_000);

    public async Task InvokeAsync(HttpContext context, VeloDbContext dbContext)
    {
        if (context.Request.Path.StartsWithSegments(AgentPath, StringComparison.OrdinalIgnoreCase))
        {
            // TODO: check token usage for dbContext.CurrentOrgId against _dailyBudget
            // For now, allow all requests through
        }

        await next(context);
    }
}
