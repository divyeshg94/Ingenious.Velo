using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Velo.Shared.Models.Ado;
using Velo.Api.Services;
using Velo.Shared.Contracts;
using Velo.Shared.Models;

namespace Velo.Api.Controllers;

/// <summary>
/// Receives Azure DevOps service hook events (build.complete).
/// ADO calls this endpoint automatically every time a pipeline run finishes.
/// No authentication — validated by the shared secret header instead.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class WebhookController(
    IMetricsRepository repo,
    DoraComputeService doraService,
    IConfiguration config,
    ILogger<WebhookController> logger) : ControllerBase
{
    private const string SecretHeader = "X-Velo-Secret";
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    [HttpPost("ado")]
    public async Task<ActionResult> AdoBuildComplete(CancellationToken cancellationToken)
    {
        // ── Verify shared secret ──────────────────────────────────────────────────
        var expectedSecret = config["Webhook:Secret"] ?? "velo-webhook-secret";
        var incomingSecret = Request.Headers[SecretHeader].FirstOrDefault();

        if (!string.Equals(incomingSecret, expectedSecret, StringComparison.Ordinal))
        {
            logger.LogWarning("WEBHOOK: Invalid or missing secret from {RemoteIp}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid webhook secret" });
        }

        // ── Parse ADO service hook payload ────────────────────────────────────────
        string body;
        using (var sr = new System.IO.StreamReader(Request.Body))
            body = await sr.ReadToEndAsync(cancellationToken);

        AdoBuildCompleteEvent? evt;
        try { evt = JsonSerializer.Deserialize<AdoBuildCompleteEvent>(body, _json); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WEBHOOK: Failed to parse payload");
            return BadRequest(new { error = "Invalid payload" });
        }

        if (evt?.EventType != "build.complete" || evt.Resource == null)
        {
            logger.LogDebug("WEBHOOK: Ignoring event type {Type}", evt?.EventType);
            return Ok(); // ADO expects 200 even for ignored events
        }

        var resource = evt.Resource;
        if (resource.Status != "completed" || resource.FinishTime == null || resource.StartTime == null)
            return Ok();

        // ── Extract org name from the account base URL ────────────────────────────
        // e.g. "https://dev.azure.com/myorg/" → "myorg"
        var baseUrl = evt.ResourceContainers?.Account?.BaseUrl ?? string.Empty;
        var orgName = ParseOrgName(baseUrl);
        var projectName = resource.Project?.Name ?? string.Empty;

        if (string.IsNullOrEmpty(orgName) || string.IsNullOrEmpty(projectName))
        {
            logger.LogWarning("WEBHOOK: Could not determine orgName or projectName from payload");
            return Ok();
        }

        logger.LogInformation(
            "WEBHOOK: build.complete received — org={Org}, project={Project}, build={Build}, result={Result}",
            orgName, projectName, resource.BuildNumber, resource.Result);

        // ── Deduplicate ───────────────────────────────────────────────────────────
        var definitionId = resource.Definition?.Id ?? 0;
        var runNumber = resource.BuildNumber ?? string.Empty;

        if (await repo.RunExistsAsync(orgName, projectName, definitionId, runNumber, cancellationToken))
        {
            logger.LogDebug("WEBHOOK: Run {Run} already exists — skipping", runNumber);
            return Ok();
        }

        // ── Store the pipeline run ────────────────────────────────────────────────
        var run = new PipelineRunDto
        {
            Id = Guid.NewGuid(),
            OrgId = orgName,
            ProjectId = projectName,
            AdoPipelineId = definitionId,
            PipelineName = resource.Definition?.Name ?? "Unknown",
            RunNumber = runNumber,
            Result = resource.Result ?? "unknown",
            StartTime = resource.StartTime.Value,
            FinishTime = resource.FinishTime,
            DurationMs = resource.FinishTime.HasValue
                ? (long)(resource.FinishTime.Value - resource.StartTime.Value).TotalMilliseconds
                : null,
            IsDeployment = IsDeploymentPipeline(resource.Definition?.Name),
            TriggeredBy = resource.RequestedBy?.DisplayName,
            IngestedAt = DateTimeOffset.UtcNow
        };

        await repo.SaveRunAsync(run, cancellationToken);

        // ── Recompute DORA metrics immediately ────────────────────────────────────
        await doraService.ComputeAndSaveAsync(orgName, projectName, cancellationToken);

        logger.LogInformation(
            "WEBHOOK: Processed run {Build} — org={Org}, project={Project}",
            runNumber, orgName, projectName);

        return Ok();
    }

    private static string ParseOrgName(string baseUrl)
    {
        // https://dev.azure.com/myorg/  →  myorg
        // https://myorg.visualstudio.com/  →  myorg
        if (baseUrl.Contains("dev.azure.com"))
        {
            var parts = baseUrl.TrimEnd('/').Split('/');
            return parts.Length >= 4 ? parts[3] : string.Empty;
        }
        if (baseUrl.Contains(".visualstudio.com"))
        {
            var host = new Uri(baseUrl).Host;
            return host.Split('.')[0];
        }
        return string.Empty;
    }

    private static bool IsDeploymentPipeline(string? name)
    {
        var n = (name ?? string.Empty).ToLowerInvariant();
        return n.Contains("deploy") || n.Contains("release") || n.Contains("prod");
    }
}
