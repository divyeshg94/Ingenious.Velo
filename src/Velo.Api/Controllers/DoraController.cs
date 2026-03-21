using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Shared.Models;
using Velo.Shared.Contracts;

namespace Velo.Api.Controllers;

/// <summary>
/// DORA Metrics API controller with comprehensive security and audit logging.
/// SECURITY: All endpoints require [Authorize] - validates JWT token from Azure AD B2C.
/// AUDIT: All operations logged with org_id, user context, and correlation ID.
/// MULTI-TENANCY: All queries automatically filtered by org_id (from JWT token → TenantResolutionMiddleware → VeloDbContext.CurrentOrgId).
/// ROW-LEVEL SECURITY: SQL Server RLS policies enforce org_id scoping at the database layer.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DoraController(IMetricsRepository metricsRepository, ILogger<DoraController> logger) : ControllerBase
{
    /// <summary>
    /// Get the latest DORA metrics for a project.
    /// Multi-tenant: Only returns metrics for the authenticated org_id.
    /// Enforced by: EF Core global query filter + SQL Server RLS.
    /// </summary>
    [HttpGet("latest")]
    public async Task<ActionResult<DoraMetricsDto>> GetLatestMetrics(
        [FromQuery] string projectId,
        CancellationToken cancellationToken = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "N/A";
        var userId = User?.FindFirst("sub")?.Value ?? "unknown";

        try
        {
            if (string.IsNullOrEmpty(orgId))
            {
                logger.LogWarning(
                    "SECURITY: Unauthorized attempt to fetch DORA metrics - OrgId missing, " +
                    "UserId: {UserId}, CorrelationId: {CorrelationId}",
                    userId, correlationId);
                return Unauthorized(new { error = "Organization context not found" });
            }

            if (string.IsNullOrWhiteSpace(projectId))
            {
                logger.LogWarning(
                    "AUDIT: Invalid projectId in DORA metrics request - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    orgId, userId, correlationId);
                return BadRequest(new { error = "projectId is required" });
            }

            logger.LogInformation(
                "AUDIT: Fetching latest DORA metrics - OrgId: {OrgId}, ProjectId: {ProjectId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, projectId, userId, correlationId);

            var metrics = await metricsRepository.GetLatestAsync(orgId, projectId, cancellationToken);

            if (metrics == null)
            {
                logger.LogInformation(
                    "AUDIT: No metrics found - OrgId: {OrgId}, ProjectId: {ProjectId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    orgId, projectId, userId, correlationId);

                // Return 200 with a status flag instead of 404 so the UI can show
                // a friendly "gathering data" message rather than an error.
                return Ok(new
                {
                    status = "gathering",
                    message = "Successfully connected! We are gathering your pipeline data. Metrics will appear after your next pipeline run.",
                    orgId,
                    projectId
                });
            }

            logger.LogInformation(
                "AUDIT: Successfully returned DORA metrics - OrgId: {OrgId}, ProjectId: {ProjectId}, " +
                "DeploymentFrequency: {DeploymentFrequency}, Rating: {Rating}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, projectId, metrics.DeploymentFrequency, metrics.DeploymentFrequencyRating, userId, correlationId);

            return Ok(metrics);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex,
                "SECURITY: Unauthorized access to DORA metrics - OrgId: {OrgId}, ProjectId: {ProjectId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, projectId, userId, correlationId);
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: Exception fetching DORA metrics - OrgId: {OrgId}, ProjectId: {ProjectId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, projectId, userId, correlationId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get DORA metrics history for a project.
    /// Multi-tenant: Only returns metrics for the authenticated org_id.
    /// Enforced by: EF Core global query filter + SQL Server RLS.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<DoraMetricsDto>>> GetMetricsHistory(
        [FromQuery] string projectId,
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "N/A";
        var userId = User?.FindFirst("sub")?.Value ?? "unknown";

        try
        {
            if (string.IsNullOrEmpty(orgId))
            {
                logger.LogWarning(
                    "SECURITY: Unauthorized attempt to fetch DORA history - OrgId missing, " +
                    "UserId: {UserId}, CorrelationId: {CorrelationId}",
                    userId, correlationId);
                return Unauthorized(new { error = "Organization context not found" });
            }

            if (string.IsNullOrWhiteSpace(projectId))
            {
                return BadRequest(new { error = "projectId is required" });
            }

            if (days < 1 || days > 365)
            {
                return BadRequest(new { error = "days must be between 1 and 365" });
            }

            logger.LogInformation(
                "AUDIT: Fetching DORA metrics history - OrgId: {OrgId}, ProjectId: {ProjectId}, Days: {Days}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, projectId, days, userId, correlationId);

            var from = DateTimeOffset.UtcNow.AddDays(-days);
            var to = DateTimeOffset.UtcNow;

            var metrics = await metricsRepository.GetHistoryAsync(orgId, projectId, from, to, cancellationToken);

            logger.LogInformation(
                "AUDIT: Successfully returned {MetricsCount} historical DORA metrics - OrgId: {OrgId}, ProjectId: {ProjectId}, Days: {Days}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                metrics.Count(), orgId, projectId, days, userId, correlationId);

            return Ok(metrics);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: Exception fetching DORA metrics history - OrgId: {OrgId}, ProjectId: {ProjectId}, Days: {Days}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, projectId, days, userId, correlationId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
