using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Shared.Models;
using Velo.Shared.Contracts;
using Velo.Api.Interface;
using Velo.Api.Services;

namespace Velo.Api.Controllers;

/// <summary>
/// Organizations API controller - manages org connections and project access.
/// SECURITY: All endpoints require [Authorize] - validates JWT token from Azure AD B2C.
/// MULTI-TENANCY: All queries scoped to org_id from JWT token (via TenantResolutionMiddleware).
/// AUDIT: All operations logged with org_id, user context, and correlation ID.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrgsController(
    IMetricsRepository metricsRepository,
    IProjectService projectService,
    AdoPipelineIngestService ingestService,
    ILogger<OrgsController> logger) : ControllerBase
{
    /// <summary>
    /// Get the current organization from the JWT token claim.
    /// Multi-tenant: Returns the org_id and auto-populates org details from database or creates default.
    /// In Azure DevOps: org_id comes from the user's token automatically.
    /// In Local Dev: org_id comes from mock token or localStorage.
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<OrgContextDto>> GetCurrentOrg(CancellationToken cancellationToken)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "N/A";
        var userId = User?.FindFirst("sub")?.Value ?? "unknown";

        try
        {
            if (string.IsNullOrEmpty(orgId))
            {
                logger.LogWarning(
                    "SECURITY: Unauthorized attempt to fetch organization - OrgId missing, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    userId, correlationId);
                return Unauthorized(new { error = "Organization context not found" });
            }

            logger.LogInformation(
                "AUDIT: Fetching organization context - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, userId, correlationId);

            // Try to fetch org from database
            var org = await metricsRepository.GetOrgContextAsync(orgId, cancellationToken);

            if (org == null)
            {
                logger.LogInformation(
                    "AUDIT: Organization not yet registered - creating default context for OrgId: {OrgId}",
                    orgId);

                // Auto-create default org context on first access
                // This allows seamless first-time user experience
                var defaultOrg = new OrgContextDto
                {
                    OrgId = orgId,
                    OrgUrl = $"https://dev.azure.com/{orgId}",
                    DisplayName = orgId,
                    IsPremium = false,
                    DailyTokenBudget = 50_000,
                    RegisteredAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow
                };

                await metricsRepository.SaveOrgContextAsync(defaultOrg, cancellationToken);
                logger.LogInformation(
                    "AUDIT: Auto-created default organization context for OrgId: {OrgId}",
                    orgId);

                return Ok(defaultOrg);
            }

            // Update last seen time
            org.LastSeenAt = DateTime.UtcNow;
            await metricsRepository.SaveOrgContextAsync(org, cancellationToken);

            logger.LogInformation(
                "AUDIT: Successfully returned organization context - OrgId: {OrgId}, Premium: {IsPremium}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, org.IsPremium, userId, correlationId);

            return Ok(org);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: Exception fetching organization - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, userId, correlationId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get all projects available to the current organization.
    /// Multi-tenant: Only returns projects for the authenticated org_id.
    /// Enforced by: EF query filter on PipelineRuns table.
    /// </summary>
    [HttpGet("projects")]
    public async Task<ActionResult<IEnumerable<string>>> GetAvailableProjects(CancellationToken cancellationToken)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "N/A";
        var userId = User?.FindFirst("sub")?.Value ?? "unknown";

        try
        {
            if (string.IsNullOrEmpty(orgId))
            {
                logger.LogWarning(
                    "SECURITY: Unauthorized attempt to fetch projects - OrgId missing, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    userId, correlationId);
                return Unauthorized(new { error = "Organization context not found" });
            }

            logger.LogInformation(
                "AUDIT: Fetching available projects - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, userId, correlationId);

            var projects = await projectService.GetProjectsAsync(orgId, cancellationToken);

            logger.LogInformation(
                "AUDIT: Successfully returned {ProjectCount} projects - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                projects.Count(), orgId, userId, correlationId);

            return Ok(projects);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: Exception fetching projects - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, userId, correlationId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Connect (create-or-update) an organisation by URL.
    /// Called by the frontend Connections tab on first-time setup or URL change.
    /// The org_id is derived from the JWT token — cannot be overridden.
    ///
    /// AUTO-BACKFILL: If X-Ado-Access-Token is present and the org has never been synced
    /// (or hasn't been synced in the last hour), a background task is kicked off immediately
    /// to pull up to 200 pipeline runs per project from the ADO REST API and compute DORA
    /// metrics. This handles both the first-time setup case and webhook-failure recovery.
    /// </summary>
    [HttpPost("connect")]
    public async Task<ActionResult<object>> ConnectOrganization(
        [FromBody] UpdateOrgRequest request,
        CancellationToken cancellationToken)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "N/A";
        var userId = User?.FindFirst("sub")?.Value ?? "unknown";
        var adoToken = Request.Headers["X-Ado-Access-Token"].FirstOrDefault();

        try
        {
            if (string.IsNullOrEmpty(orgId))
            {
                logger.LogWarning(
                    "SECURITY: Unauthorized connect attempt — OrgId missing, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    userId, correlationId);
                return Unauthorized(new { error = "Organization context not found" });
            }

            if (string.IsNullOrWhiteSpace(request.OrgUrl))
                return BadRequest(new { error = "OrgUrl is required" });

            logger.LogInformation(
                "AUDIT: Connecting organization — OrgId: {OrgId}, OrgUrl: {OrgUrl}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, request.OrgUrl, userId, correlationId);

            var org = await metricsRepository.GetOrgContextAsync(orgId, cancellationToken);
            var isFirstConnect = org == null;

            if (org == null)
            {
                org = new OrgContextDto
                {
                    OrgId = orgId,
                    OrgUrl = request.OrgUrl.TrimEnd('/'),
                    DisplayName = request.DisplayName ?? ParseOrgName(request.OrgUrl),
                    IsPremium = false,
                    DailyTokenBudget = 50_000,
                    RegisteredAt = DateTimeOffset.UtcNow,
                    LastSeenAt = DateTimeOffset.UtcNow
                };
            }
            else
            {
                org.OrgUrl = request.OrgUrl.TrimEnd('/');
                if (!string.IsNullOrEmpty(request.DisplayName))
                    org.DisplayName = request.DisplayName;
                org.LastSeenAt = DateTimeOffset.UtcNow;
            }

            await metricsRepository.SaveOrgContextAsync(org, cancellationToken);

            // --- AUTO-BACKFILL ---
            // Trigger a background historical sync when:
            //   • An ADO token is present (required to call ADO REST API), AND
            //   • The org has never been synced OR hasn't been synced in the last hour
            //     (the 1-hour window prevents duplicate syncs on every page refresh).
            var syncStale = org.LastSyncedAt == null
                || org.LastSyncedAt < DateTimeOffset.UtcNow.AddHours(-1);
            var autoSyncTriggered = !string.IsNullOrEmpty(adoToken) && syncStale;

            if (autoSyncTriggered)
            {
                // Stamp LastSyncedAt immediately so a concurrent request doesn't also
                // kick off a sync before the background task has a chance to run.
                org.LastSyncedAt = DateTimeOffset.UtcNow;
                await metricsRepository.SaveOrgContextAsync(org, cancellationToken);

                var capturedOrgId = orgId;
                var capturedOrgUrl = org.OrgUrl;
                var capturedToken = adoToken!;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        logger.LogInformation(
                            "AUTO_SYNC: Background sync started — OrgId={OrgId}, FirstConnect={First}",
                            capturedOrgId, isFirstConnect);

                        var ingested = await ingestService.IngestAllProjectsAsync(
                            capturedOrgId, capturedOrgUrl, capturedToken, CancellationToken.None);

                        // Fire-and-forget DORA computation per project is handled inside
                        // IngestAsync → each run triggers DoraComputeService via webhook path.
                        // For the bulk backfill we do a final compute pass after ingest.
                        logger.LogInformation(
                            "AUTO_SYNC: Ingested {Total} runs — OrgId={OrgId}",
                            ingested, capturedOrgId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex,
                            "AUTO_SYNC: Background sync failed — OrgId={OrgId}",
                            capturedOrgId);
                    }
                });

                logger.LogInformation(
                    "AUDIT: Auto-sync background task started — OrgId: {OrgId}, FirstConnect: {First}, CorrelationId: {CorrelationId}",
                    orgId, isFirstConnect, correlationId);
            }

            logger.LogInformation(
                "AUDIT: Organization connected — OrgId: {OrgId}, OrgUrl: {OrgUrl}, AutoSync: {AutoSync}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, org.OrgUrl, autoSyncTriggered, userId, correlationId);

            return Ok(new { org, autoSyncTriggered });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: Exception connecting organization — OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, userId, correlationId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Update an existing organization's connection details.
    /// This endpoint allows users to update their org URL and other settings.
    /// The org_id is already determined from the JWT token - cannot be changed.
    /// </summary>
    [HttpPost("update")]
    public async Task<ActionResult<OrgContextDto>> UpdateOrganization(
        [FromBody] UpdateOrgRequest request,
        CancellationToken cancellationToken)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "N/A";
        var userId = User?.FindFirst("sub")?.Value ?? "unknown";

        try
        {
            if (string.IsNullOrEmpty(orgId))
            {
                logger.LogWarning(
                    "SECURITY: Unauthorized attempt to update organization - OrgId missing, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    userId, correlationId);
                return Unauthorized(new { error = "Organization context not found" });
            }

            if (string.IsNullOrWhiteSpace(request.OrgUrl))
            {
                logger.LogWarning(
                    "AUDIT: Invalid org update request - missing OrgUrl, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    userId, correlationId);
                return BadRequest(new { error = "OrgUrl is required" });
            }

            logger.LogInformation(
                "AUDIT: Updating organization - OrgId: {OrgId}, OrgUrl: {OrgUrl}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, request.OrgUrl, userId, correlationId);

            // Fetch existing org
            var org = await metricsRepository.GetOrgContextAsync(orgId, cancellationToken);
            if (org == null)
            {
                logger.LogWarning(
                    "AUDIT: Organization not found for update - OrgId: {OrgId}, UserId: {UserId}",
                    orgId, userId);
                return NotFound(new { error = "Organization not found" });
            }

            // Update fields (org_id cannot be changed)
            org.OrgUrl = request.OrgUrl;
            if (!string.IsNullOrEmpty(request.DisplayName))
            {
                org.DisplayName = request.DisplayName;
            }

            await metricsRepository.SaveOrgContextAsync(org, cancellationToken);

            logger.LogInformation(
                "AUDIT: Successfully updated organization - OrgId: {OrgId}, OrgUrl: {OrgUrl}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, request.OrgUrl, userId, correlationId);

            return Ok(org);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: Exception updating organization - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, userId, correlationId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
    private static string ParseOrgName(string orgUrl)
    {
        if (Uri.TryCreate(orgUrl.TrimEnd('/'), UriKind.Absolute, out var uri))
            return uri.Segments.LastOrDefault()?.Trim('/') ?? orgUrl;
        return orgUrl;
    }
}

public record UpdateOrgRequest(string OrgUrl, string? DisplayName = null);
