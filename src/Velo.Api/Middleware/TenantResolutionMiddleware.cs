using System.Security.Claims;
using Serilog.Context;
using Velo.SQL;
using Microsoft.EntityFrameworkCore;

namespace Velo.Api.Middleware;

/// <summary>
/// Tenant resolution middleware - extracts org_id from JWT token and sets it on HttpContext.
/// SECURITY: Logs all org_id resolution attempts (successful and failed) for audit trail.
/// CRITICAL: This is where security violations are detected (missing org_id, invalid org_id).
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
    private const string AzureAdOidClaim = "oid"; // Azure AD object ID = org identifier

    public async Task InvokeAsync(HttpContext context, VeloDbContext dbContext)
    {
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "N/A";

        try
        {
            // Extract org_id from JWT token (Azure AD B2C 'oid' claim = object ID)
            var orgIdClaim = context.User?.FindFirst(AzureAdOidClaim)?.Value 
                          ?? context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(orgIdClaim))
            {
                // SECURITY ALERT: Unauthorized access attempt
                logger.LogWarning(
                    "SECURITY: Unauthorized access attempt - missing org_id claim. " +
                    "User: {User}, Path: {Path}, CorrelationId: {CorrelationId}",
                    context.User?.Identity?.Name ?? "Anonymous",
                    context.Request.Path,
                    correlationId);

                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Organization context not found" });
                return;
            }

            // Store org_id on HttpContext for use in controllers
            context.Items["OrgId"] = orgIdClaim;

            // Push to Serilog context (all subsequent logs will include this org_id)
            LogContext.PushProperty("OrgId", orgIdClaim);

            // Set org_id on DbContext for EF Core query filtering
            dbContext.CurrentOrgId = orgIdClaim;

            // Set SQL Server session context for RLS enforcement
            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText = "EXEC sp_set_session_context N'org_id', @OrgId";
            command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@OrgId", orgIdClaim));

            try
            {
                await command.ExecuteNonQueryAsync();
                
                logger.LogDebug(
                    "SECURITY: Tenant context resolved - OrgId: {OrgId}, CorrelationId: {CorrelationId}",
                    orgIdClaim, correlationId);
            }
            catch (Exception ex)
            {
                // SECURITY ALERT: Failed to set RLS context
                logger.LogError(ex,
                    "SECURITY: Failed to set SQL Server session context for OrgId: {OrgId}, CorrelationId: {CorrelationId}",
                    orgIdClaim, correlationId);
                throw;
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
