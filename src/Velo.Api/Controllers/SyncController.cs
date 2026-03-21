using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Api.Services;

namespace Velo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SyncController(
    AdoPipelineIngestService ingestService,
    DoraComputeService doraService,
    AdoServiceHookService hookService,
    ILogger<SyncController> logger) : ControllerBase
{
    private const string AdoTokenHeader = "X-Ado-Access-Token";

    /// <summary>
    /// Pull pipeline runs from Azure DevOps, compute DORA metrics, and register
    /// the service hook so future runs are ingested automatically.
    /// POST /api/sync/{projectId}
    /// </summary>
    [HttpPost("{projectId}")]
    public async Task<ActionResult> Sync(string projectId, CancellationToken cancellationToken)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var adoToken = Request.Headers[AdoTokenHeader].FirstOrDefault();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "N/A";

        if (string.IsNullOrEmpty(orgId))
            return Unauthorized(new { error = "Organization context not found" });

        if (string.IsNullOrEmpty(adoToken))
            return BadRequest(new { error = $"Missing {AdoTokenHeader} header. Supply the token from SDK.getAccessToken()." });

        logger.LogInformation(
            "SYNC: Starting for OrgId={OrgId}, ProjectId={ProjectId}, CorrelationId={CorrelationId}",
            orgId, projectId, correlationId);

        try
        {
            // ── 1. Pull historical pipeline runs ─────────────────────────────────
            var ingested = await ingestService.IngestAsync(orgId, projectId, adoToken, cancellationToken);

            // ── 2. Compute DORA metrics from stored runs ──────────────────────────
            var metrics = await doraService.ComputeAndSaveAsync(orgId, projectId, cancellationToken);

            // ── 3. Register service hook so future runs are automatic ─────────────
            var webhookBase = $"{Request.Scheme}://{Request.Host}";
            var hookRegistered = await hookService.EnsureHookRegisteredAsync(
                orgId, projectId, adoToken, webhookBase, cancellationToken);

            logger.LogInformation(
                "SYNC: Done — {Ingested} runs ingested, hook={Hook}, OrgId={OrgId}, ProjectId={ProjectId}",
                ingested, hookRegistered ? "registered" : "not registered (check permissions)", orgId, projectId);

            return Ok(new
            {
                ingested,
                metrics,
                hookRegistered,
                hookNote = hookRegistered
                    ? "Service hook registered — future pipeline runs will be ingested automatically."
                    : "Could not register service hook automatically. " +
                      "Go to ADO → Project Settings → Service hooks → Create subscription → " +
                      "Web Hooks → build.complete → POST to /api/webhook/ado.",
                orgId,
                projectId,
                syncedAt = DateTimeOffset.UtcNow
            });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "SYNC: ADO API error for OrgId={OrgId}, ProjectId={ProjectId}", orgId, projectId);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SYNC: Unexpected error for OrgId={OrgId}, ProjectId={ProjectId}", orgId, projectId);
            return StatusCode(500, new { error = "Sync failed. Check application logs." });
        }
    }
}
