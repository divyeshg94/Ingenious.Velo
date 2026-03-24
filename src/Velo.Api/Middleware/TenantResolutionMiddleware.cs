using System.Security.Claims;
using Serilog.Context;
using Velo.SQL;
using Microsoft.EntityFrameworkCore;

namespace Velo.Api.Middleware;

/// <summary>
/// Tenant resolution middleware - extracts org_id from the request and sets it on HttpContext.
/// SECURITY: Logs all org_id resolution attempts (successful and failed) for audit trail.
/// CRITICAL: This is where security violations are detected (missing org_id, invalid org_id).
/// 
/// Resolution order:
/// 1. X-Azure-DevOps-OrgId header  (set by the ADO extension frontend via SDK.getHost().name)
/// 2. JWT 'oid' claim              (Azure AD tokens, if ever used)
/// 3. JWT NameIdentifier claim     (fallback)
/// 
/// This org_id is then used by:
/// 1. VeloDbContext.CurrentOrgId - automatic EF Core query filtering
/// 2. SQL Server session context - RLS policy enforcement at database layer
/// 
/// SECURITY: This middleware ensures all database queries are automatically scoped to the authenticated org_id.
/// Never call database without org_id set - it indicates a security misconfiguration.
/// </summary>
public class TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
{
    private const string OrgIdHeader = "X-Azure-DevOps-OrgId";

    public async Task InvokeAsync(HttpContext context, VeloDbContext dbContext)
    {
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "N/A";

        try
        {
            // If the user is not authenticated yet, skip tenant resolution entirely.
            // The [Authorize] attribute on controllers will return the proper 401 challenge.
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                logger.LogDebug(
                    "TENANT: Skipping — user not authenticated. Path: {Path}, CorrelationId: {CorrelationId}",
                    context.Request.Path, correlationId);
                await next(context);
                return;
            }

            // 1. Prefer explicit header from the ADO extension frontend (SDK.getHost().name)
            var orgId = context.Request.Headers[OrgIdHeader].FirstOrDefault();

            // 2. VSSO token 'tid' claim — the Azure DevOps tenant/org GUID
            if (string.IsNullOrEmpty(orgId))
                orgId = context.User.FindFirst("tid")?.Value;

            // 3. Azure AD 'oid' claim (if Azure AD tokens are ever used)
            if (string.IsNullOrEmpty(orgId))
                orgId = context.User.FindFirst("oid")?.Value;

            if (string.IsNullOrEmpty(orgId))
            {
                logger.LogWarning(
                    "TENANT: Authenticated user but no org_id found. " +
                    "Header={Header}, Claims=[{Claims}], Path={Path}, CorrelationId={CorrelationId}",
                    context.Request.Headers[OrgIdHeader].FirstOrDefault() ?? "(none)",
                    string.Join(", ", context.User.Claims.Select(c => $"{c.Type}={c.Value}")),
                    context.Request.Path,
                    correlationId);

                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Organization context not found" });
                return;
            }

            logger.LogInformation(
                "TENANT: Resolved OrgId={OrgId} for Path={Path}, CorrelationId={CorrelationId}",
                orgId, context.Request.Path, correlationId);

            // Store org_id on HttpContext for use in controllers
            context.Items["OrgId"] = orgId;

            // Push to Serilog context (all subsequent logs will include this org_id)
            LogContext.PushProperty("OrgId", orgId);

            // Set org_id on DbContext for EF Core query filtering
            dbContext.CurrentOrgId = orgId;

            // Set SQL Server session context for RLS enforcement.
            // Only applicable when using a relational provider (skipped for InMemory in tests).
            if (dbContext.Database.IsRelational())
            {
                var connection = dbContext.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                using var command = connection.CreateCommand();
                command.CommandText = "EXEC sp_set_session_context N'org_id', @OrgId";
                command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@OrgId", orgId));

                try
                {
                    await command.ExecuteNonQueryAsync();

                    logger.LogDebug(
                        "SECURITY: Tenant context resolved - OrgId: {OrgId}, CorrelationId: {CorrelationId}",
                        orgId, correlationId);
                }
                catch (Exception ex)
                {
                    // SECURITY ALERT: Failed to set RLS context
                    logger.LogError(ex,
                        "SECURITY: Failed to set SQL Server session context for OrgId: {OrgId}, CorrelationId: {CorrelationId}",
                        orgId, correlationId);
                    throw;
                }
            }

            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "SECURITY: Unhandled exception in tenant resolution middleware, CorrelationId: {CorrelationId}",
                correlationId);
            throw;
        }
    }
}
