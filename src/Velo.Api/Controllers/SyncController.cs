using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Api.Services;
using Velo.Shared.Models;

namespace Velo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SyncController(
    IAdoPipelineIngestService ingestService,
    IDoraComputeService doraService,
    IAdoServiceHookService hookService,
    ILogger<SyncController> logger) : ControllerBase
{
    private const string AdoTokenHeader = "X-Ado-Access-Token";

    /// <summary>POST /api/sync/{projectId} — pull historical runs, compute DORA, register build + PR webhooks.</summary>
    [HttpPost("{projectId}")]
    public async Task<IActionResult> Sync(string projectId, CancellationToken cancellationToken)
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

            // Build webhook registration — best-effort
            WebhookStatusDto hookStatus;
            try
            {
                hookStatus = await hookService.RegisterAsync(orgId, projectId, adoToken, webhookBase, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SYNC: Build hook registration threw — returning error status");
                hookStatus = new WebhookStatusDto
                {
                    IsRegistered = false,
                    RegistrationError = ex.Message,
                    ManualSetupUrl = $"https://dev.azure.com/{orgId}/_settings/serviceHooks"
                };
            }

            // PR webhook registration — best-effort (separate subscriptions for created + updated)
            WebhookStatusDto prHookStatus;
            try
            {
                prHookStatus = await hookService.RegisterPrHookAsync(orgId, projectId, adoToken, webhookBase, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SYNC: PR hook registration threw — returning error status");
                prHookStatus = new WebhookStatusDto
                {
                    IsRegistered = false,
                    RegistrationError = ex.Message,
                    ManualSetupUrl = $"https://dev.azure.com/{orgId}/_settings/serviceHooks"
                };
            }

            prHookStatus ??= new WebhookStatusDto
            {
                IsRegistered = false,
                ManualSetupUrl = $"https://dev.azure.com/{orgId}/_settings/serviceHooks"
            };

            // Work item webhook registration — best-effort (workitem.updated → rework-rate tracking)
            WebhookStatusDto workItemHookStatus;
            try
            {
                workItemHookStatus = await hookService.RegisterWorkItemHookAsync(orgId, projectId, adoToken, webhookBase, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SYNC: Work item hook registration threw — returning error status");
                workItemHookStatus = new WebhookStatusDto
                {
                    IsRegistered = false,
                    RegistrationError = ex.Message,
                    ManualSetupUrl = $"https://dev.azure.com/{orgId}/_settings/serviceHooks"
                };
            }

            workItemHookStatus ??= new WebhookStatusDto
            {
                IsRegistered = false,
                ManualSetupUrl = $"https://dev.azure.com/{orgId}/_settings/serviceHooks"
            };

            logger.LogInformation(
                "SYNC: Done — {Ingested} runs ingested, buildHook={BHook}, prHook={PHook}, workItemHook={WIHook}, " +
                "OrgId={OrgId}, ProjectId={ProjectId}",
                ingested, hookStatus.IsRegistered, prHookStatus.IsRegistered, workItemHookStatus.IsRegistered,
                orgId, projectId);

            return new OkObjectResult(new
            {
                ingested,
                metrics,
                webhook        = hookStatus,
                prWebhook      = prHookStatus,
                workItemWebhook = workItemHookStatus,
                orgId,
                projectId,
                syncedAt       = DateTimeOffset.UtcNow
            });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "SYNC: ADO API error for OrgId={OrgId}, ProjectId={ProjectId}", orgId, projectId);
            return new BadRequestObjectResult(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SYNC: Unexpected error for OrgId={OrgId}, ProjectId={ProjectId}", orgId, projectId);
            return new ObjectResult(new { error = "Sync failed. Check application logs." }) { StatusCode = 500 };
        }
    }

    // ── Build webhook endpoints ────────────────────────────────────────────────

    /// <summary>GET /api/sync/hook-status/{projectId} — check build webhook status.</summary>
    [HttpGet("hook-status/{projectId}")]
    public async Task<IActionResult> GetHookStatus(string projectId, CancellationToken cancellationToken)
    {
        var (orgId, adoToken, error) = GetOrgAndToken();
        if (error != null) return error;

        var webhookBase = $"{Request.Scheme}://{Request.Host}";
        var status = await hookService.GetStatusAsync(orgId!, projectId, adoToken!, webhookBase, cancellationToken);
        return Ok(status);
    }

    /// <summary>POST /api/sync/hook/{projectId} — register (or re-register) the build webhook.</summary>
    [HttpPost("hook/{projectId}")]
    public async Task<IActionResult> RegisterHook(string projectId, CancellationToken cancellationToken)
    {
        var (orgId, adoToken, error) = GetOrgAndToken();
        if (error != null) return error;

        var webhookBase = $"{Request.Scheme}://{Request.Host}";
        var status = await hookService.RegisterAsync(orgId!, projectId, adoToken!, webhookBase, cancellationToken);
        return status.IsRegistered ? Ok(status) : StatusCode(422, status);
    }

    /// <summary>DELETE /api/sync/hook/{subscriptionId} — remove a webhook subscription (build or PR).</summary>
    [HttpDelete("hook/{subscriptionId}")]
    public async Task<IActionResult> RemoveHook(string subscriptionId, CancellationToken cancellationToken)
    {
        var (orgId, adoToken, error) = GetOrgAndToken();
        if (error != null) return error;

        var removed = await hookService.RemoveAsync(orgId!, subscriptionId, adoToken!, cancellationToken);
        return removed ? NoContent() : StatusCode(500, new { error = "Failed to remove subscription." });
    }

    // ── PR webhook endpoints ───────────────────────────────────────────────────

    /// <summary>GET /api/sync/pr-hook-status/{projectId} — check PR webhook registration status.</summary>
    [HttpGet("pr-hook-status/{projectId}")]
    public async Task<IActionResult> GetPrHookStatus(string projectId, CancellationToken cancellationToken)
    {
        var (orgId, adoToken, error) = GetOrgAndToken();
        if (error != null) return error;

        var webhookBase = $"{Request.Scheme}://{Request.Host}";
        var status = await hookService.GetPrStatusAsync(orgId!, projectId, adoToken!, webhookBase, cancellationToken);
        return Ok(status);
    }

    /// <summary>POST /api/sync/pr-hook/{projectId} — register (or re-register) the PR webhooks.</summary>
    [HttpPost("pr-hook/{projectId}")]
    public async Task<IActionResult> RegisterPrHook(string projectId, CancellationToken cancellationToken)
    {
        var (orgId, adoToken, error) = GetOrgAndToken();
        if (error != null) return error;

        var webhookBase = $"{Request.Scheme}://{Request.Host}";
        var status = await hookService.RegisterPrHookAsync(orgId!, projectId, adoToken!, webhookBase, cancellationToken);
        return status.IsRegistered ? Ok(status) : StatusCode(422, status);
    }

    // ── Work item webhook endpoints ────────────────────────────────────────────

    /// <summary>GET /api/sync/workitem-hook-status/{projectId} — check workitem.updated webhook status.</summary>
    [HttpGet("workitem-hook-status/{projectId}")]
    public async Task<IActionResult> GetWorkItemHookStatus(string projectId, CancellationToken cancellationToken)
    {
        var (orgId, adoToken, error) = GetOrgAndToken();
        if (error != null) return error;

        var webhookBase = $"{Request.Scheme}://{Request.Host}";
        var status = await hookService.GetWorkItemHookStatusAsync(orgId!, projectId, adoToken!, webhookBase, cancellationToken);
        return Ok(status);
    }

    /// <summary>POST /api/sync/workitem-hook/{projectId} — register (or re-register) the workitem.updated webhook.</summary>
    [HttpPost("workitem-hook/{projectId}")]
    public async Task<IActionResult> RegisterWorkItemHook(string projectId, CancellationToken cancellationToken)
    {
        var (orgId, adoToken, error) = GetOrgAndToken();
        if (error != null) return error;

        var webhookBase = $"{Request.Scheme}://{Request.Host}";
        var status = await hookService.RegisterWorkItemHookAsync(orgId!, projectId, adoToken!, webhookBase, cancellationToken);
        return status.IsRegistered ? Ok(status) : StatusCode(422, status);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (string? orgId, string? adoToken, ActionResult? error) GetOrgAndToken()
    {
        var orgId    = HttpContext.Items["OrgId"]?.ToString();
        var adoToken = Request.Headers[AdoTokenHeader].FirstOrDefault();

        if (string.IsNullOrEmpty(orgId))
            return (null, null, Unauthorized(new { error = "Organization context not found" }));

        if (string.IsNullOrEmpty(adoToken))
            return (null, null, BadRequest(new { error = $"Missing {AdoTokenHeader} header." }));

        return (orgId, adoToken, null);
    }
}
