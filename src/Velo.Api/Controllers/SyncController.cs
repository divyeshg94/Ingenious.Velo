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

    /// <summary>POST /api/sync/{projectId} — pull historical runs, compute DORA, register webhook.</summary>
    [HttpPost("{projectId}")]
    public async Task<ActionResult> Sync(string projectId, CancellationToken cancellationToken)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var adoToken = Request.Headers[AdoTokenHeader].FirstOrDefault();

        if (string.IsNullOrEmpty(orgId))
            return Unauthorized(new { error = "Organization context not found" });

        if (string.IsNullOrEmpty(adoToken))
            return BadRequest(new { error = $"Missing {AdoTokenHeader} header." });

        logger.LogInformation("SYNC: Starting for OrgId={OrgId}, ProjectId={ProjectId}", orgId, projectId);

        try
        {
            var ingested = await ingestService.IngestAsync(orgId, projectId, adoToken, cancellationToken);
            var metrics = await doraService.ComputeAndSaveAsync(orgId, projectId, cancellationToken);

            var webhookBase = $"{Request.Scheme}://{Request.Host}";
            var hookStatus = await hookService.RegisterAsync(orgId, projectId, adoToken, webhookBase, cancellationToken);

            logger.LogInformation(
                "SYNC: Done — {Ingested} runs ingested, hook={Hook}, OrgId={OrgId}, ProjectId={ProjectId}",
                ingested, hookStatus.IsRegistered, orgId, projectId);

            return Ok(new { ingested, metrics, webhook = hookStatus, orgId, projectId, syncedAt = DateTimeOffset.UtcNow });
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

    /// <summary>GET /api/sync/hook-status/{projectId} — check whether the webhook is registered.</summary>
    [HttpGet("hook-status/{projectId}")]
    public async Task<ActionResult> GetHookStatus(string projectId, CancellationToken cancellationToken)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var adoToken = Request.Headers[AdoTokenHeader].FirstOrDefault();

        if (string.IsNullOrEmpty(orgId))
            return Unauthorized(new { error = "Organization context not found" });

        if (string.IsNullOrEmpty(adoToken))
            return BadRequest(new { error = $"Missing {AdoTokenHeader} header." });

        var webhookBase = $"{Request.Scheme}://{Request.Host}";
        var status = await hookService.GetStatusAsync(orgId, projectId, adoToken, webhookBase, cancellationToken);
        return Ok(status);
    }

    /// <summary>POST /api/sync/hook/{projectId} — register (or re-register) the webhook.</summary>
    [HttpPost("hook/{projectId}")]
    public async Task<ActionResult> RegisterHook(string projectId, CancellationToken cancellationToken)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var adoToken = Request.Headers[AdoTokenHeader].FirstOrDefault();

        if (string.IsNullOrEmpty(orgId))
            return Unauthorized(new { error = "Organization context not found" });

        if (string.IsNullOrEmpty(adoToken))
            return BadRequest(new { error = $"Missing {AdoTokenHeader} header." });

        var webhookBase = $"{Request.Scheme}://{Request.Host}";
        var status = await hookService.RegisterAsync(orgId, projectId, adoToken, webhookBase, cancellationToken);
        return status.IsRegistered ? Ok(status) : StatusCode(422, status);
    }

    /// <summary>DELETE /api/sync/hook/{subscriptionId} — remove the webhook subscription.</summary>
    [HttpDelete("hook/{subscriptionId}")]
    public async Task<ActionResult> RemoveHook(string subscriptionId, CancellationToken cancellationToken)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var adoToken = Request.Headers[AdoTokenHeader].FirstOrDefault();

        if (string.IsNullOrEmpty(orgId))
            return Unauthorized(new { error = "Organization context not found" });

        if (string.IsNullOrEmpty(adoToken))
            return BadRequest(new { error = $"Missing {AdoTokenHeader} header." });

        var removed = await hookService.RemoveAsync(orgId, subscriptionId, adoToken, cancellationToken);
        return removed ? NoContent() : StatusCode(500, new { error = "Failed to remove subscription." });
    }
}
