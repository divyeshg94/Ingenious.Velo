using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Shared.Models;
using Velo.Shared.Contracts;
using Velo.Api.Services;

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
public class DoraController(
    IMetricsRepository metricsRepository,
    AdoPipelineIngestService ingestService,
    DoraComputeService doraComputeService,
    ILogger<DoraController> logger) : ControllerBase
{
    private const string AdoTokenHeader = "X-Ado-Access-Token";

    /// <summary>
    /// Get the latest DORA metrics for a project.
    /// Multi-tenant: Only returns metrics for the authenticated org_id.
    /// Enforced by: EF Core global query filter + SQL Server RLS.
    ///
    /// AUTO-RECOVERY: When no metrics exist and X-Ado-Access-Token is present,
    /// automatically triggers a background sync for this project so metrics appear
    /// without requiring the user to visit the Connections tab first. This recovers
    /// from webhook failures or missed events without any manual intervention.
    /// </summary>
    [HttpGet("latest")]
    public async Task<ActionResult<DoraMetricsDto>> GetLatestMetrics(
        [FromQuery] string projectId,
        [FromQuery] string? repositoryName = null,
        [FromQuery] string? teamName = null,
        CancellationToken cancellationToken = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "N/A";
        var userId = User?.FindFirst("sub")?.Value ?? "unknown";
        var adoToken = Request.Headers[AdoTokenHeader].FirstOrDefault();

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
                "AUDIT: Fetching latest DORA metrics - OrgId: {OrgId}, ProjectId: {ProjectId}, " +
                "RepositoryName: {RepositoryName}, TeamName: {TeamName}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, projectId, repositoryName ?? "(all)", teamName ?? "(all)", userId, correlationId);

            var metrics = await metricsRepository.GetLatestAsync(orgId, projectId, cancellationToken);

            if (metrics == null)
            {
                logger.LogInformation(
                    "AUDIT: No metrics found - OrgId: {OrgId}, ProjectId: {ProjectId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    orgId, projectId, userId, correlationId);

                // AUTO-RECOVERY: If the ADO token is present, kick off a background sync
                // for this specific project. This handles:
                //   1. First-time load before any webhook has fired
                //   2. Missed webhook events (delivery failures from ADO)
                //   3. Any gap in data that left this project without metrics
                if (!string.IsNullOrEmpty(adoToken))
                {
                    var capturedOrgId = orgId;
                    var capturedProjectId = projectId;
                    var capturedToken = adoToken;
                    var capturedRepo = repositoryName;
                    var capturedTeam = teamName;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            logger.LogInformation(
                                "AUTO_RECOVERY: Background sync triggered from dora/latest — OrgId={OrgId}, ProjectId={ProjectId}, " +
                                "RepositoryName={RepositoryName}, TeamName={TeamName}",
                                capturedOrgId, capturedProjectId, capturedRepo ?? "(all)", capturedTeam ?? "(all)");

                            var ingested = await ingestService.IngestAsync(
                                capturedOrgId, capturedProjectId, capturedToken, CancellationToken.None);

                            if (ingested > 0)
                                await doraComputeService.ComputeAndSaveAsync(
                                    capturedOrgId, capturedProjectId, CancellationToken.None);

                            logger.LogInformation(
                                "AUTO_RECOVERY: Done — {Ingested} runs ingested, OrgId={OrgId}, ProjectId={ProjectId}",
                                ingested, capturedOrgId, capturedProjectId);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex,
                                "AUTO_RECOVERY: Background sync failed — OrgId={OrgId}, ProjectId={ProjectId}",
                                capturedOrgId, capturedProjectId);
                        }
                    });

                    // Return "syncing" so the UI can show a progress indicator and poll
                    return Ok(new
                    {
                        status = "syncing",
                        message = "Syncing your pipeline history — metrics will appear in a few seconds.",
                        orgId,
                        projectId
                    });
                }

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
        [FromQuery] string? repositoryName = null,
        [FromQuery] string? teamName = null,
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
                "AUDIT: Fetching DORA metrics history - OrgId: {OrgId}, ProjectId: {ProjectId}, Days: {Days}, " +
                "RepositoryName: {RepositoryName}, TeamName: {TeamName}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, projectId, days, repositoryName ?? "(all)", teamName ?? "(all)", userId, correlationId);

            var from = DateTimeOffset.UtcNow.AddDays(-days);
            var to = DateTimeOffset.UtcNow;

            var metrics = await metricsRepository.GetHistoryAsync(orgId, projectId, from, to, cancellationToken);

            logger.LogInformation(
                "AUDIT: Successfully returned {MetricsCount} historical DORA metrics - OrgId: {OrgId}, ProjectId: {ProjectId}, Days: {Days}, " +
                "RepositoryName: {RepositoryName}, TeamName: {TeamName}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                metrics.Count(), orgId, projectId, days, repositoryName ?? "(all)", teamName ?? "(all)", userId, correlationId);

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
