using Velo.Api.Data;

namespace Velo.Api.Middleware;

/// <summary>
/// Resolves the Azure DevOps organization ID from the bearer token and sets it
/// on the scoped VeloDbContext so all queries are automatically filtered by tenant.
/// </summary>
public class TenantResolutionMiddleware(RequestDelegate next)
{
    private const string OrgIdClaimType = "oid"; // Azure AD object ID used as org identifier

    public async Task InvokeAsync(HttpContext context, VeloDbContext dbContext)
    {
        var orgId = context.User?.FindFirst(OrgIdClaimType)?.Value
                    ?? context.Request.Headers["X-Velo-OrgId"].FirstOrDefault();

        if (string.IsNullOrEmpty(orgId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing organization identity.");
            return;
        }

        dbContext.CurrentOrgId = orgId;
        await next(context);
    }
}
