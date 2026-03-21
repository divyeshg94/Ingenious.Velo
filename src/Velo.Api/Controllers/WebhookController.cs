using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Velo.Shared.Models.Ado;
using Velo.Api.Services;
using Velo.Shared.Contracts;
using Velo.Shared.Models;
using Velo.SQL;
using Microsoft.EntityFrameworkCore;

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
    VeloDbContext dbContext,
    DoraComputeService doraService,
    IConfiguration config,
    ILogger<WebhookController> logger) : ControllerBase
{
    private const string SecretHeader = "X-Velo-Secret";
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    [HttpPost("ado")]
    public async Task<ActionResult> AdoBuildComplete(CancellationToken cancellationToken)
    {
        // Verify shared secret
        var expectedSecret = config["Webhook:Secret"] ?? "velo-webhook-secret";
        var incomingSecret = Request.Headers[SecretHeader].FirstOrDefault();

        if (!string.Equals(incomingSecret, expectedSecret, StringComparison.Ordinal))
        {
            logger.LogWarning("WEBHOOK: Invalid or missing secret from {RemoteIp}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid webhook secret" });
        }

        // Read raw body
        string body;
        using (var sr = new System.IO.StreamReader(Request.Body))
            body = await sr.ReadToEndAsync(cancellationToken);

        // Log a preview so we can diagnose any JSON mapping issues
        logger.LogDebug("WEBHOOK: Raw payload ({Length} bytes): {Preview}",
            body.Length, body.Length > 800 ? body[..800] + "..." : body);

        // Deserialize
        AdoBuildCompleteEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<AdoBuildCompleteEvent>(body, _json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WEBHOOK: Failed to deserialize payload (length={Length})", body.Length);
            return BadRequest(new { error = "Invalid payload" });
        }

        // Log every field we care about so nothing is silently skipped
        logger.LogInformation(
            "WEBHOOK: Payload parsed -- EventType={EventType}, HasResource={HasResource}",
            evt?.EventType ?? "(null)", evt?.Resource != null);

        if (evt?.EventType != "build.complete" || evt.Resource == null)
        {
            logger.LogInformation(
                "WEBHOOK: Skipping -- EventType={EventType} is not 'build.complete' or resource is null",
                evt?.EventType ?? "(null)");
            return Ok();
        }

        var resource = evt.Resource;

        logger.LogInformation(
            "WEBHOOK: Resource -- Status={Status}, Result={Result}, BuildNumber={Build}, " +
            "StartTime={Start}, FinishTime={Finish}",
            resource.Status, resource.Result, resource.BuildNumber,
            resource.StartTime, resource.FinishTime);

        // Guard against builds still running
        if (resource.FinishTime == null || resource.StartTime == null)
        {
            logger.LogInformation(
                "WEBHOOK: Skipping -- StartTime or FinishTime is null. Status={Status}, Build={Build}",
                resource.Status, resource.BuildNumber);
            return Ok();
        }

        // ADO sends status="completed" for finished builds, but accept any finished-state string
        // so older ADO versions that send the result string directly also work.
        var finished = resource.Status is "completed" or "succeeded"
                                       or "failed" or "partiallySucceeded" or "canceled";
        if (!finished)
        {
            logger.LogInformation(
                "WEBHOOK: Skipping -- build not in a finished state. Status={Status}, Build={Build}",
                resource.Status, resource.BuildNumber);
            return Ok();
        }

        // Org: modern payloads include baseUrl in resourceContainers; legacy ones only have id.
        // Fall back to parsing org directly from resource.Url when baseUrl is absent.
        var baseUrl = evt.ResourceContainers?.Account?.BaseUrl
                   ?? evt.ResourceContainers?.Collection?.BaseUrl
                   ?? string.Empty;

        var orgName = ParseOrgName(baseUrl);
        if (string.IsNullOrEmpty(orgName))
            orgName = ParseOrgName(resource.Url ?? string.Empty);

        // Project: full fallback chain
        //   1. resource.project.name              — modern YAML builds
        //   2. project segment of resource.url    — dev.azure.com & visualstudio.com
        //   3. project segment of definition.url  — same URL shape, secondary chance
        //   4. resourceContainers.project.id      — GUID, present in all ADO service hook formats
        var projectName = resource.Project?.Name
                       ?? ParseProjectFromUrl(resource.Url)
                       ?? ParseProjectFromUrl(resource.Definition?.Url)
                       ?? evt.ResourceContainers?.Project?.Id
                       ?? string.Empty;

        // Result: legacy XAML builds omit resource.result; resource.status carries the outcome
        var result = resource.Result
                  ?? MapStatusToResult(resource.Status)
                  ?? "unknown";

        // TriggeredBy: modern YAML builds use resource.requestedBy;
        // legacy XAML builds put the identity in resource.requests[].requestedFor
        var triggeredBy = resource.RequestedBy?.DisplayName
                       ?? resource.Requests?.FirstOrDefault()?.RequestedFor?.DisplayName;

        logger.LogInformation(
            "WEBHOOK: Context -- OrgName={OrgName}, ProjectName={ProjectName}, " +
            "BaseUrl={BaseUrl}, ResourceUrl={ResourceUrl}",
            orgName.Length > 0 ? orgName : "(empty)",
            projectName.Length > 0 ? projectName : "(empty)",
            baseUrl.Length > 0 ? baseUrl : "(empty)",
            resource.Url ?? "(null)");

        if (string.IsNullOrEmpty(orgName) || string.IsNullOrEmpty(projectName))
        {
            logger.LogWarning(
                "WEBHOOK: Could not extract org or project. " +
                "OrgName={OrgName}, ProjectName={ProjectName}, " +
                "BaseUrl={BaseUrl}, ResourceUrl={ResourceUrl}, " +
                "ResourceProjectName={ResourceProjectName}, ContainerProjectId={ContainerProjectId}, " +
                "AccountNull={AccountNull}, CollectionNull={CollectionNull}",
                orgName.Length > 0 ? orgName : "(empty)",
                projectName.Length > 0 ? projectName : "(empty)",
                baseUrl.Length > 0 ? baseUrl : "(empty)",
                resource.Url ?? "(null)",
                resource.Project?.Name ?? "(null)",
                evt.ResourceContainers?.Project?.Id ?? "(null)",
                evt.ResourceContainers?.Account == null,
                evt.ResourceContainers?.Collection == null);
            return Ok();
        }

        // Set tenant context so EF query filters and SQL Server RLS work correctly
        await SetTenantContextAsync(orgName, cancellationToken);

        var definitionId = resource.Definition?.Id ?? 0;
        var runNumber = resource.BuildNumber ?? string.Empty;

        // Deduplicate
        if (await repo.RunExistsAsync(orgName, projectName, definitionId, runNumber, cancellationToken))
        {
            logger.LogInformation(
                "WEBHOOK: Run already exists -- skipping. Org={Org}, Project={Project}, Build={Build}",
                orgName, projectName, runNumber);
            return Ok();
        }

        // Save the run
        var run = new PipelineRunDto
        {
            Id = Guid.NewGuid(),
            OrgId = orgName,
            ProjectId = projectName,
            AdoPipelineId = definitionId,
            PipelineName = resource.Definition?.Name ?? "Unknown",
            RunNumber = runNumber,
            Result = result,
            StartTime = resource.StartTime.Value,
            FinishTime = resource.FinishTime,
            DurationMs = resource.FinishTime.HasValue
                ? (long)(resource.FinishTime.Value - resource.StartTime.Value).TotalMilliseconds
                : null,
            IsDeployment = IsDeploymentPipeline(resource.Definition?.Name),
            TriggeredBy = triggeredBy,
            IngestedAt = DateTimeOffset.UtcNow
        };

        try
        {
            await repo.SaveRunAsync(run, cancellationToken);
            logger.LogInformation(
                "WEBHOOK: Saved run -- Org={Org}, Project={Project}, Build={Build}, Result={Result}",
                orgName, projectName, runNumber, run.Result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "WEBHOOK: Failed to save run -- Org={Org}, Project={Project}, Build={Build}",
                orgName, projectName, runNumber);
            return StatusCode(500, new { error = "Failed to save pipeline run" });
        }

        // Recompute DORA metrics (non-fatal if this fails)
        try
        {
            await doraService.ComputeAndSaveAsync(orgName, projectName, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "WEBHOOK: DORA recompute failed (run was saved). Org={Org}, Project={Project}",
                orgName, projectName);
        }

        return Ok();
    }

    private static string ParseOrgName(string baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl)) return string.Empty;

        // https://dev.azure.com/myorg/  ->  myorg
        if (baseUrl.Contains("dev.azure.com"))
        {
            var parts = baseUrl.TrimEnd('/').Split('/');
            return parts.Length >= 4 ? parts[3] : string.Empty;
        }

        // https://myorg.visualstudio.com/  ->  myorg
        if (baseUrl.Contains(".visualstudio.com"))
        {
            var host = new Uri(baseUrl).Host;
            return host.Split('.')[0];
        }

        return string.Empty;
    }

    /// <summary>
    /// Extracts the project name/id from a build or definition resource URL.
    /// dev.azure.com : https://dev.azure.com/{org}/{project}/_apis/...
    /// visualstudio  : https://{org}.visualstudio.com/DefaultCollection/{projectGuid}/_apis/...
    ///                 https://{org}.visualstudio.com/{project}/_apis/...
    /// </summary>
    private static string? ParseProjectFromUrl(string? resourceUrl)
    {
        if (string.IsNullOrEmpty(resourceUrl)) return null;

        var parts = resourceUrl.Split('/');

        if (resourceUrl.Contains("dev.azure.com"))
        {
            // [0]=https: [1]='' [2]=dev.azure.com [3]=org [4]=project
            var project = parts.Length >= 5 ? parts[4] : null;
            return string.IsNullOrEmpty(project) || project.StartsWith('_') ? null : project;
        }

        if (resourceUrl.Contains(".visualstudio.com"))
        {
            // With DefaultCollection: .../DefaultCollection/{project-name-or-guid}/...
            if (parts.Length >= 5 &&
                string.Equals(parts[3], "DefaultCollection", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrEmpty(parts[4]) ? null : parts[4];

            // Without collection segment: .../{project}/...
            return parts.Length >= 4 && !string.IsNullOrEmpty(parts[3]) ? parts[3] : null;
        }

        return null;
    }

    /// <summary>
    /// Maps build status to a result string for legacy XAML builds that
    /// omit the separate result field and use status for both.
    /// </summary>
    private static string? MapStatusToResult(string? status) => status switch
    {
        "succeeded" => "succeeded",
        "failed" => "failed",
        "canceled" => "canceled",
        "partiallySucceeded" => "partiallySucceeded",
        _ => null
    };

    private static bool IsDeploymentPipeline(string? name)
    {
        var n = (name ?? string.Empty).ToLowerInvariant();
        return n.Contains("deploy") || n.Contains("release") || n.Contains("prod");
    }

    /// <summary>
    /// Sets CurrentOrgId on the DbContext and calls sp_set_session_context so that
    /// SQL Server RLS policies allow the INSERT -- mirrors TenantResolutionMiddleware.
    /// </summary>
    private async Task SetTenantContextAsync(string orgId, CancellationToken cancellationToken)
    {
        dbContext.CurrentOrgId = orgId;

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "EXEC sp_set_session_context N'org_id', @OrgId";
        cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@OrgId", orgId));

        try
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            logger.LogDebug("WEBHOOK: Tenant context set for OrgId={OrgId}", orgId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "WEBHOOK: Failed to set SQL session context for OrgId={OrgId} -- continuing anyway",
                orgId);
        }
    }
}
