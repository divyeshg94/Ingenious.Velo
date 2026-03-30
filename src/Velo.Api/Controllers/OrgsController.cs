using System.Collections.Concurrent;
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
    IAdoPipelineIngestService ingestService,
    ILogger<OrgsController> logger) : ControllerBase
{
    // One entry per org — value is 1 if a background sync is running, absent/0 if free.
    // Prevents a burst of POST /api/orgs/connect calls from spawning parallel sync tasks.
    private static readonly ConcurrentDictionary<string, byte> _activeSyncs = new(StringComparer.OrdinalIgnoreCase);

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
                    SanitiseForLog(userId), SanitiseForLog(correlationId));
                return Unauthorized(new { error = "Organization context not found" });
            }

            logger.LogInformation(
                "AUDIT: Fetching organization context - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                SanitiseForLog(orgId), SanitiseForLog(userId), SanitiseForLog(correlationId));

            // Try to fetch org from database
            var org = await metricsRepository.GetOrgContextAsync(orgId, cancellationToken);

            if (org == null)
            {
                logger.LogInformation(
                    "AUDIT: Organization not yet registered - creating default context for OrgId: {OrgId}",
                    SanitiseForLog(orgId));

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
                    SanitiseForLog(orgId));

                return Ok(defaultOrg);
            }

            // Update last seen time
            org.LastSeenAt = DateTime.UtcNow;
            await metricsRepository.SaveOrgContextAsync(org, cancellationToken);

            logger.LogInformation(
                "AUDIT: Successfully returned organization context - OrgId: {OrgId}, Premium: {IsPremium}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                SanitiseForLog(orgId), org.IsPremium, SanitiseForLog(userId), SanitiseForLog(correlationId));

            return Ok(org);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: Exception fetching organization - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                SanitiseForLog(orgId), SanitiseForLog(userId), SanitiseForLog(correlationId));
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
                    SanitiseForLog(userId), SanitiseForLog(correlationId));
                return Unauthorized(new { error = "Organization context not found" });
            }

            logger.LogInformation(
                "AUDIT: Fetching available projects - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                SanitiseForLog(orgId), SanitiseForLog(userId), SanitiseForLog(correlationId));

            var projects = await projectService.GetProjectsAsync(orgId, cancellationToken);

            logger.LogInformation(
                "AUDIT: Successfully returned {ProjectCount} projects - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                projects.Count(), SanitiseForLog(orgId), SanitiseForLog(userId), SanitiseForLog(correlationId));

            return Ok(projects);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: Exception fetching projects - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                SanitiseForLog(orgId), SanitiseForLog(userId), SanitiseForLog(correlationId));
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
                    SanitiseForLog(userId), SanitiseForLog(correlationId));
                return Unauthorized(new { error = "Organization context not found" });
            }

            if (string.IsNullOrWhiteSpace(request.OrgUrl))
                return BadRequest(new { error = "OrgUrl is required" });

            // SSRF guard — only allow legitimate Azure DevOps URLs.
            if (!IsAllowedAdoUrl(request.OrgUrl))
                return BadRequest(new { error = "OrgUrl must be a valid Azure DevOps URL (https://dev.azure.com/... or https://[org].visualstudio.com)." });

            // Sanitise OrgUrl before logging to prevent log injection via crafted URLs.
            var safeOrgUrl = SanitiseForLog(request.OrgUrl);
            logger.LogInformation(
                "AUDIT: Connecting organization — OrgId: {OrgId}, OrgUrl: {OrgUrl}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                SanitiseForLog(orgId), safeOrgUrl, SanitiseForLog(userId), SanitiseForLog(correlationId));

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

                // DDoS guard: allow at most ONE background sync per org at a time.
                // TryAdd returns false if another sync is already running → skip silently.
                if (_activeSyncs.TryAdd(capturedOrgId, 1))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            logger.LogInformation(
                                "AUTO_SYNC: Background sync started — OrgId={OrgId}, FirstConnect={First}",
                                SanitiseForLog(capturedOrgId), isFirstConnect);

                            var ingested = await ingestService.IngestAllProjectsAsync(
                                capturedOrgId, capturedOrgUrl, capturedToken, CancellationToken.None);

                            logger.LogInformation(
                                "AUTO_SYNC: Ingested {Total} runs — OrgId={OrgId}",
                                ingested, SanitiseForLog(capturedOrgId));
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex,
                                "AUTO_SYNC: Background sync failed — OrgId={OrgId}",
                                SanitiseForLog(capturedOrgId));
                        }
                        finally
                        {
                            // Always release the slot so future syncs can run.
                            _activeSyncs.TryRemove(capturedOrgId, out _);
                        }
                    });
                }
                else
                {
                    logger.LogInformation(
                        "AUTO_SYNC: Skipped — sync already running for OrgId={OrgId}",
                        SanitiseForLog(capturedOrgId));
                }

                logger.LogInformation(
                    "AUDIT: Auto-sync background task started — OrgId: {OrgId}, FirstConnect: {First}, CorrelationId: {CorrelationId}",
                    SanitiseForLog(orgId), isFirstConnect, SanitiseForLog(correlationId));
            }

            logger.LogInformation(
                "AUDIT: Organization connected — OrgId: {OrgId}, OrgUrl: {OrgUrl}, AutoSync: {AutoSync}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                SanitiseForLog(orgId), SanitiseForLog(org.OrgUrl), autoSyncTriggered, SanitiseForLog(userId), SanitiseForLog(correlationId));

            return Ok(new { org, autoSyncTriggered });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: Exception connecting organization — OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                SanitiseForLog(orgId), SanitiseForLog(userId), SanitiseForLog(correlationId));
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
                    SanitiseForLog(userId), SanitiseForLog(correlationId));
                return Unauthorized(new { error = "Organization context not found" });
            }

            if (string.IsNullOrWhiteSpace(request.OrgUrl))
            {
                logger.LogWarning(
                    "AUDIT: Invalid org update request - missing OrgUrl, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    SanitiseForLog(userId), SanitiseForLog(correlationId));
                return BadRequest(new { error = "OrgUrl is required" });
            }

            if (!IsAllowedAdoUrl(request.OrgUrl))
                return BadRequest(new { error = "OrgUrl must be a valid Azure DevOps URL (https://dev.azure.com/... or https://[org].visualstudio.com)." });

            logger.LogInformation(
                "AUDIT: Updating organization - OrgId: {OrgId}, OrgUrl: {OrgUrl}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                SanitiseForLog(orgId), SanitiseForLog(request.OrgUrl), SanitiseForLog(userId), SanitiseForLog(correlationId));

            // Fetch existing org
            var org = await metricsRepository.GetOrgContextAsync(orgId, cancellationToken);
            if (org == null)
            {
                logger.LogWarning(
                    "AUDIT: Organization not found for update - OrgId: {OrgId}, UserId: {UserId}",
                    SanitiseForLog(orgId), SanitiseForLog(userId));
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
                SanitiseForLog(orgId), SanitiseForLog(request.OrgUrl), SanitiseForLog(userId), SanitiseForLog(correlationId));

            return Ok(org);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: Exception updating organization - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                SanitiseForLog(orgId), SanitiseForLog(userId), SanitiseForLog(correlationId));
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
    /// <summary>
    /// SSRF guard: only allow Azure DevOps origin URLs.
    /// Blocks requests to internal/private IP ranges or non-ADO hosts being used as proxies.
    /// </summary>
    private static bool IsAllowedAdoUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;

        // Must be HTTPS
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            return false;

        var host = uri.Host.ToLowerInvariant();

        // dev.azure.com/org
        if (host == "dev.azure.com") return true;

        // org.visualstudio.com
        if (host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private static string ParseOrgName(string orgUrl)
    {
        if (Uri.TryCreate(orgUrl.TrimEnd('/'), UriKind.Absolute, out var uri))
            return uri.Segments.LastOrDefault()?.Trim('/') ?? orgUrl;
        return orgUrl;
    }

    /// <summary>
    /// Strips newlines and control characters from a value before it is written to a log entry.
    /// Prevents log-injection attacks where a crafted string containing CRLF could create
    /// forged log lines in text-based log sinks or corrupt structured JSON output.
    /// </summary>
    private static string SanitiseForLog(string? value) =>
        Velo.Api.Logging.LogSanitizer.SanitiseForLog(value);
}

public record UpdateOrgRequest(string OrgUrl, string? DisplayName = null);
