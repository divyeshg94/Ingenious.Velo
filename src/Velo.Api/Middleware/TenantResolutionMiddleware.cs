using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Serilog.Context;
using Velo.SQL;

namespace Velo.Api.Middleware;

/// <summary>
/// Resolves the org_id for every authenticated request and binds it to the HTTP context,
/// the EF Core DbContext (query filters), and SQL Server session context (RLS).
///
/// SECURITY — Resolution &amp; validation order:
///   1. X-Azure-DevOps-OrgId header   — provided by the ADO extension SDK (SDK.getHost().name)
///   2. JWT 'tid' claim               — AAD tenant GUID carried in VSSO app tokens
///   3. JWT 'oid' claim               — fallback for older token formats
///
/// TENANT BINDING (anti-spoofing):
///   On the first request for a newly registered org we record the AAD tenant GUID (tid) from the
///   JWT and store it in the Organizations table (AadTenantId).
///   On every subsequent request we verify the incoming JWT's tid matches the stored value.
///   This prevents an attacker from sending a forged X-Azure-DevOps-OrgId header and accessing
///   a different organisation's data with their own valid token.
/// </summary>
public class TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
{
    private const string OrgIdHeader = "X-Azure-DevOps-OrgId";

    public async Task InvokeAsync(HttpContext context, VeloDbContext dbContext)
    {
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "N/A";

        try
        {
            // Unauthenticated requests: let the [Authorize] attribute return the proper 401 challenge.
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                logger.LogDebug(
                    "TENANT: Skipping — user not authenticated. Path={Path}, CorrelationId={CorrelationId}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(context.Request.Path.Value ?? "/"), correlationId);
                await next(context);
                return;
            }

            // ── Step 1: Resolve candidate orgId ──────────────────────────────────
            // Prefer the explicit header (SDK.getHost().name = org display name).
            var orgId = context.Request.Headers[OrgIdHeader].FirstOrDefault()?.Trim();

            // Extract AAD tenant GUID from the JWT 'tid' claim — used for binding validation.
            var jwtTid = context.User.FindFirst("tid")?.Value?.Trim();

            // If the header is absent, fall back to the JWT claims.
            if (string.IsNullOrEmpty(orgId))
                orgId = jwtTid;

            if (string.IsNullOrEmpty(orgId))
                orgId = context.User.FindFirst("oid")?.Value?.Trim();

            if (string.IsNullOrEmpty(orgId))
            {
                logger.LogWarning(
                    "TENANT: Authenticated user but no org_id resolved. " +
                    "Header={Header}, Path={Path}, CorrelationId={CorrelationId}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(context.Request.Headers[OrgIdHeader].FirstOrDefault() ?? "(none)"),
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(context.Request.Path.Value ?? "/"), correlationId);

                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Organization context not found" });
                return;
            }

            // ── Step 2: Tenant binding validation ────────────────────────────────
            // Only applies to relational providers (skipped for InMemory in tests).
            if (dbContext.Database.IsRelational())
            {
                var org = await dbContext.Organizations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.OrgId == orgId && !o.IsDeleted);

                if (org is not null)
                {
                    if (!string.IsNullOrEmpty(org.AadTenantId) && !string.IsNullOrEmpty(jwtTid))
                    {
                        // Org already has a bound tenant — validate the incoming token belongs to it.
                        if (!string.Equals(org.AadTenantId, jwtTid, StringComparison.OrdinalIgnoreCase))
                        {
                            logger.LogWarning(
                                "SECURITY: Tenant mismatch — OrgId={OrgId} is bound to tenant {Stored} " +
                                "but request carries tid={Incoming}. Possible spoofing attempt. " +
                                "CorrelationId={CorrelationId}",
                                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(org.AadTenantId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(jwtTid), correlationId);

                            context.Response.StatusCode = 403;
                            await context.Response.WriteAsJsonAsync(new { error = "Access denied" });
                            return;
                        }
                    }
                    else if (string.IsNullOrEmpty(org.AadTenantId) && !string.IsNullOrEmpty(jwtTid))
                    {
                        // First authenticated request for this org — bind its AAD tenant.
                        var orgToUpdate = await dbContext.Organizations
                            .FirstOrDefaultAsync(o => o.OrgId == orgId && !o.IsDeleted);

                        if (orgToUpdate is not null)
                        {
                            orgToUpdate.AadTenantId = jwtTid;
                            orgToUpdate.ModifiedBy  = "system:tenant-bind";
                            orgToUpdate.ModifiedDate = DateTimeOffset.UtcNow;
                            await dbContext.SaveChangesAsync();

                            logger.LogInformation(
                                "SECURITY: Bound OrgId={OrgId} to AadTenantId={TenantId}. CorrelationId={CorrelationId}",
                                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(jwtTid), correlationId);
                        }
                    }
                }
                // If org is not found, let the request proceed — controllers will return 404/401 as appropriate.
            }

            // ── Step 3: Apply tenant context ──────────────────────────────────────
            logger.LogInformation(
                "TENANT: Resolved OrgId={OrgId}, Path={Path}, CorrelationId={CorrelationId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(context.Request.Path.Value ?? "/"), correlationId);

            context.Items["OrgId"] = orgId;
            LogContext.PushProperty("OrgId", orgId);
            dbContext.CurrentOrgId = orgId;

            // Set SQL Server session context for RLS enforcement.
            if (dbContext.Database.IsRelational())
            {
                var connection = dbContext.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                    await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandText = "EXEC sp_set_session_context N'org_id', @OrgId";
                command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@OrgId", orgId));

                // SECURITY: If we cannot set the RLS session context we MUST abort the request.
                // Continuing without it would allow the request to read/write rows for ALL orgs.
                await command.ExecuteNonQueryAsync();

                logger.LogDebug(
                    "SECURITY: RLS session context set — OrgId={OrgId}, CorrelationId={CorrelationId}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), correlationId);
            }

            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "SECURITY: Unhandled exception in TenantResolutionMiddleware. CorrelationId={CorrelationId}",
                correlationId);
            throw;
        }
    }
}
