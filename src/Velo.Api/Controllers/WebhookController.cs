using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Velo.Shared.Models.Ado;
using Velo.Api.Services;
using Velo.Shared.Contracts;
using Velo.Shared.Models;
using Velo.SQL;
using Microsoft.EntityFrameworkCore;

namespace Velo.Api.Controllers;

/// <summary>
/// Receives Azure DevOps service hook events (build.complete, git.pullrequest.*).
/// ADO calls this endpoint automatically every time a pipeline run finishes or a PR changes state.
/// No authentication — validated by the shared secret header instead.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class WebhookController(
    IMetricsRepository repo,
    VeloDbContext dbContext,
    IDoraComputeService doraService,
    IConfiguration config,
    ILogger<WebhookController> logger) : ControllerBase
{
    private const string SecretHeader = "X-Velo-Secret";
    private const int MaxBodyBytes = 10 * 1024 * 1024; // 10 MB — ADO payloads are always well under this
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    [HttpPost("ado")]
    public async Task<ActionResult> AdoEvent(CancellationToken cancellationToken)
    {
        // ── Verify shared secret (timing-safe comparison) ───────────────────────
        var expectedSecret = config["Webhook:Secret"] ?? string.Empty;
        var incomingSecret = Request.Headers[SecretHeader].FirstOrDefault() ?? string.Empty;

        // Use fixed-time comparison to prevent timing-based secret oracle attacks.
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSecret);
        var incomingBytes = Encoding.UTF8.GetBytes(incomingSecret);

        // Length-equalise before comparing to keep FixedTimeEquals happy (it requires equal lengths).
        // If lengths differ the Equals call is skipped but we still take constant time for the message.
        var secretValid = expectedBytes.Length == incomingBytes.Length &&
                          CryptographicOperations.FixedTimeEquals(expectedBytes, incomingBytes);

        if (!secretValid)
        {
            logger.LogWarning("WEBHOOK: Invalid or missing secret from {RemoteIp}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid webhook secret" });
        }

        // ── Body size guard (DoS protection) ────────────────────────────────────
        if (Request.ContentLength.HasValue && Request.ContentLength > MaxBodyBytes)
        {
            logger.LogWarning(
                "WEBHOOK: Rejected oversized payload ({Bytes} bytes) from {RemoteIp}",
                Request.ContentLength, HttpContext.Connection.RemoteIpAddress);
            return StatusCode(413, new { error = "Payload too large" });
        }

        // Read body with a hard cap using a manual size-capped stream wrapper.
        string body;
        using (var ms = new System.IO.MemoryStream())
        {
            var buffer    = new byte[8192];
            var totalRead = 0;
            int read;
            while ((read = await Request.Body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                totalRead += read;
                if (totalRead > MaxBodyBytes)
                {
                    logger.LogWarning(
                        "WEBHOOK: Rejected oversized streaming payload from {RemoteIp}",
                        HttpContext.Connection.RemoteIpAddress);
                    return StatusCode(413, new { error = "Payload too large" });
                }
                ms.Write(buffer, 0, read);
            }
            body = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }

        // Log a preview so we can diagnose any JSON mapping issues
        logger.LogDebug("WEBHOOK: Raw payload ({Length} bytes): {Preview}",
            body.Length, body.Length > 800 ? body[..800] + "..." : body);

        // Peek at eventType to dispatch to the right handler
        string? eventType = null;
        try
        {
            using var peek = JsonDocument.Parse(body);
            if (peek.RootElement.TryGetProperty("eventType", out var et))
                eventType = et.GetString();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WEBHOOK: Failed to deserialize payload (length={Length})", body.Length);
            return BadRequest(new { error = "Invalid payload" });
        }

        logger.LogInformation("WEBHOOK: EventType={EventType}", eventType ?? "(null)");

        return eventType switch
        {
            "build.complete"              => await HandleBuildCompleteAsync(body, cancellationToken),
            "git.pullrequest.created"     => await HandlePrEventAsync(body, cancellationToken),
            "git.pullrequest.updated"     => await HandlePrEventAsync(body, cancellationToken),
            "workitem.updated"            => await HandleWorkItemUpdatedAsync(body, cancellationToken),
            _ => Ok(new { skipped = true, eventType })
        };
    }

    // ── Build complete handler ──────────────────────────────────────────────────────

    private async Task<ActionResult> HandleBuildCompleteAsync(string body, CancellationToken cancellationToken)
    {
        // Deserialize
        AdoBuildCompleteEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<AdoBuildCompleteEvent>(body, _json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WEBHOOK BUILD: Failed to deserialize payload");
            return BadRequest(new { error = "Invalid build payload" });
        }

        logger.LogInformation(
            "WEBHOOK BUILD: Payload parsed -- HasResource={HasResource}",
            evt?.Resource != null);

        if (evt?.Resource == null)
        {
            logger.LogInformation("WEBHOOK BUILD: Skipping -- resource is null");
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

        // ADO registers service hooks with a project GUID, so the project segment in the
        // webhook URL/containers is always a GUID — resolve it to the human name that sync
        // stored, so both paths write to the same ProjectId and no duplicate project appears.
        if (Guid.TryParse(projectName, out _) && definitionId > 0)
        {
            var resolved = await ResolveProjectNameAsync(orgName, definitionId, cancellationToken);
            if (!string.IsNullOrEmpty(resolved))
            {
                logger.LogInformation(
                    "WEBHOOK: Resolved project GUID {Guid} -> {Name} via existing runs",
                    projectName, resolved);
                projectName = resolved;
            }
            else
            {
                logger.LogWarning(
                    "WEBHOOK: Could not resolve project GUID {Guid} for Org={Org}, DefinitionId={DefinitionId}. " +
                    "Run sync first so the project name is recorded.",
                    projectName, orgName, definitionId);
            }
        }

        // Direct GUID lookup via ProjectMappings (works even before first sync runs per pipeline)
        if (Guid.TryParse(projectName, out _))
        {
            var mapped = await dbContext.ProjectMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.OrgId == orgName && m.ProjectGuid == projectName, cancellationToken);
            if (mapped is not null)
            {
                logger.LogInformation(
                    "WEBHOOK: Resolved project GUID {Guid} -> {Name} via ProjectMappings table",
                    projectName, mapped.ProjectName);
                projectName = mapped.ProjectName;
            }
        }

        var runNumber = resource.BuildNumber ?? string.Empty;

        // Deduplicate
        if (await repo.RunExistsAsync(orgName, projectName, definitionId, runNumber, cancellationToken))
        {
            logger.LogInformation(
                "WEBHOOK: Run already exists -- skipping. Org={Org}, Project={Project}, Build={Build}",
                orgName, projectName, runNumber);
            return Ok();
        }

        // Resolve the repository name from previously-ingested runs for this pipeline.
        // The webhook path has no ADO token, so we rely on the name resolved during sync.
        var repoName = await ResolveRepoFromExistingRunsAsync(orgName, definitionId, cancellationToken);

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
            RepositoryName = repoName,
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

    // ── Pull Request event handler ─────────────────────────────────────────────────

    private async Task<ActionResult> HandlePrEventAsync(string body, CancellationToken cancellationToken)
    {
        AdoPrEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<AdoPrEvent>(body, _json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WEBHOOK PR: Failed to deserialize payload");
            return BadRequest(new { error = "Invalid PR payload" });
        }

        if (evt?.Resource == null)
        {
            logger.LogInformation("WEBHOOK PR: Skipping — resource is null");
            return Ok();
        }

        var resource = evt.Resource;

        // Org from resourceContainers.account.baseUrl
        var baseUrl = evt.ResourceContainers?.Account?.BaseUrl
                   ?? evt.ResourceContainers?.Collection?.BaseUrl
                   ?? string.Empty;

        var orgName = ParseOrgName(baseUrl);

        // Project from resource.repository.project.name
        var projectName = resource.Repository?.Project?.Name
                       ?? evt.ResourceContainers?.Project?.Id
                       ?? string.Empty;

        if (string.IsNullOrEmpty(orgName) || string.IsNullOrEmpty(projectName))
        {
            logger.LogWarning(
                "WEBHOOK PR: Could not extract org/project. BaseUrl={BaseUrl}, RepoProjName={ProjName}",
                baseUrl, resource.Repository?.Project?.Name ?? "(null)");
            return Ok();
        }

        // Resolve project GUID → name if needed
        if (Guid.TryParse(projectName, out _))
        {
            var resolved = await ResolveProjectNameFromPrAsync(orgName, projectName, cancellationToken);
            if (!string.IsNullOrEmpty(resolved)) projectName = resolved;
        }

        await SetTenantContextAsync(orgName, cancellationToken);

        var status     = resource.Status ?? "active";
        var isApproved = resource.Reviewers?.Any(r => r.Vote >= 10) ?? false;

        var prDto = new PullRequestEventDto
        {
            Id            = Guid.NewGuid(),
            OrgId         = orgName,
            ProjectId     = projectName,
            PrId          = resource.PullRequestId,
            Title         = resource.Title,
            Status        = status,
            SourceBranch  = resource.SourceRefName,
            TargetBranch  = resource.TargetRefName,
            CreatedAt     = resource.CreationDate,
            ClosedAt      = resource.ClosedDate,
            IsApproved    = isApproved,
            ReviewerCount = resource.Reviewers?.Length ?? 0,
            IngestedAt    = DateTimeOffset.UtcNow
        };

        try
        {
            await repo.SavePrEventAsync(prDto, cancellationToken);
            logger.LogInformation(
                "WEBHOOK PR: Saved — Org={Org}, Project={Project}, PrId={PrId}, Status={Status}, Approved={Approved}",
                orgName, projectName, resource.PullRequestId, status, isApproved);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "WEBHOOK PR: Failed to save — Org={Org}, Project={Project}, PrId={PrId}",
                orgName, projectName, resource.PullRequestId);
            return StatusCode(500, new { error = "Failed to save PR event" });
        }

        return Ok();
    }

    // ── Work item state-transition handler ─────────────────────────────────────────

    private async Task<ActionResult> HandleWorkItemUpdatedAsync(string body, CancellationToken cancellationToken)
    {
        AdoWorkItemEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<AdoWorkItemEvent>(body, _json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WEBHOOK WI: Failed to deserialize work item payload");
            return BadRequest(new { error = "Invalid work item payload" });
        }

        if (evt?.Resource is null)
        {
            logger.LogInformation("WEBHOOK WI: Skipping — resource is null");
            return Ok();
        }

        var resource = evt.Resource;

        // Only persist events where System.State actually changed
        var fields = resource.Fields ?? new Dictionary<string, AdoFieldChange>();
        if (!fields.TryGetValue("System.State", out var stateChange) ||
            stateChange.OldValue == stateChange.NewValue)
        {
            logger.LogDebug("WEBHOOK WI: No state change — skipping WI={WI}", resource.WorkItemId);
            return Ok();
        }

        var orgId = evt.ResourceContainers?.Collection?.Id
                 ?? evt.ResourceContainers?.Account?.Id;

        if (string.IsNullOrEmpty(orgId))
        {
            logger.LogWarning("WEBHOOK WI: Missing org in resourceContainers");
            return Ok();
        }

        fields.TryGetValue("System.WorkItemType", out var typeChange);
        var workItemType = typeChange?.NewValue ?? typeChange?.OldValue;

        // Resolve projectId from resource containers, then from the embedded workItem fields
        var projectId = evt.ResourceContainers?.Project?.Id;
        if (resource.WorkItem?.Fields is not null &&
            resource.WorkItem.Fields.TryGetValue("System.TeamProject", out var project))
            projectId = project;

        if (string.IsNullOrEmpty(projectId))
        {
            logger.LogWarning("WEBHOOK WI: Could not resolve projectId for WI={WI}", resource.WorkItemId);
            return Ok();
        }

        await SetTenantContextAsync(orgId, cancellationToken);

        var dto = new Velo.Shared.Models.WorkItemEventDto
        {
            Id           = Guid.NewGuid(),
            OrgId        = orgId,
            ProjectId    = projectId,
            WorkItemId   = resource.WorkItemId,
            WorkItemType = workItemType,
            OldState     = stateChange.OldValue,
            NewState     = stateChange.NewValue,
            ChangedAt    = resource.RevisedDate,
            IngestedAt   = DateTimeOffset.UtcNow
        };

        try
        {
            await repo.SaveWorkItemEventAsync(dto, cancellationToken);
            logger.LogInformation(
                "WEBHOOK WI: Saved — Org={Org}, Project={Project}, WI={WI}, {Old}→{New}",
                orgId, projectId, resource.WorkItemId, stateChange.OldValue, stateChange.NewValue);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "WEBHOOK WI: Failed to save — Org={Org}, Project={Project}, WI={WI}",
                orgId, projectId, resource.WorkItemId);
            return StatusCode(500, new { error = "Failed to save work item event" });
        }

        return Ok();
    }

    /// <summary>
    /// When a PR event arrives with a project GUID, try to resolve the human name
    /// from existing PipelineRun records (which are stored with human names after sync),
    /// then fall back to the ProjectMappings table populated during sync.
    /// </summary>
    private async Task<string?> ResolveProjectNameFromPrAsync(
        string orgId, string projectGuid, CancellationToken cancellationToken)
    {
        // First try: look for a non-GUID ProjectId in existing runs for this org
        var candidates = await dbContext.PipelineRuns
            .AsNoTracking()
            .Where(r => r.OrgId == orgId)
            .Select(r => r.ProjectId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var fromRuns = candidates.FirstOrDefault(p => !Guid.TryParse(p, out _));
        if (!string.IsNullOrEmpty(fromRuns)) return fromRuns;

        // Second try: look up the specific GUID in the ProjectMappings table
        var mapping = await dbContext.ProjectMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.OrgId == orgId && m.ProjectGuid == projectGuid, cancellationToken);
        return mapping?.ProjectName;
    }

    private static string ParseOrgName(string url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;

        // https://dev.azure.com/myorg/ or https://dev.azure.com/myorg/myproject/_apis/...
        if (url.Contains("dev.azure.com"))
        {
            var parts = url.TrimEnd('/').Split('/');
            return parts.Length >= 4 ? parts[3] : string.Empty;
        }

        // https://myorg.visualstudio.com/ or https://myorg.visualstudio.com/DefaultCollection/...
        if (url.Contains(".visualstudio.com"))
        {
            var host = new Uri(url).Host;
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

    /// <summary>
    /// ADO registers service hooks with a project GUID, so webhook payloads always carry
    /// a GUID for the project. This method looks up the human-readable project name from
    /// runs that sync already stored for the same pipeline, ensuring both code paths write
    /// to the same ProjectId and no duplicate project row appears in the connections view.
    /// </summary>
    private async Task<string?> ResolveProjectNameAsync(
        string orgId, int adoPipelineId, CancellationToken cancellationToken)
    {
        // First try: look for a non-GUID ProjectId in existing runs for this pipeline
        var candidates = await dbContext.PipelineRuns
            .AsNoTracking()
            .Where(r => r.OrgId == orgId && r.AdoPipelineId == adoPipelineId)
            .Select(r => r.ProjectId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var fromRuns = candidates.FirstOrDefault(p => !Guid.TryParse(p, out _));
        if (!string.IsNullOrEmpty(fromRuns)) return fromRuns;

        // Second try: look up any GUID candidate in the ProjectMappings table
        var guidCandidates = candidates.Where(p => Guid.TryParse(p, out _)).ToList();
        foreach (var guid in guidCandidates)
        {
            var mapping = await dbContext.ProjectMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.OrgId == orgId && m.ProjectGuid == guid, cancellationToken);
            if (mapping is not null) return mapping.ProjectName;
        }

        return null;
    }

    /// <summary>
    /// Looks up the repository name from previously-ingested runs for the same pipeline.
    /// The webhook path has no ADO token, so we rely on the name resolved during sync.
    /// Returns null if no previous run has a repository name set.
    /// </summary>
    private async Task<string?> ResolveRepoFromExistingRunsAsync(
        string orgId, int adoPipelineId, CancellationToken cancellationToken)
    {
        return await dbContext.PipelineRuns
            .AsNoTracking()
            .Where(r => r.OrgId == orgId && r.AdoPipelineId == adoPipelineId
                     && r.RepositoryName != null && r.RepositoryName != string.Empty)
            .Select(r => r.RepositoryName)
            .FirstOrDefaultAsync(cancellationToken);
    }

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

        // sp_set_session_context is SQL Server-specific; skip for non-relational providers (e.g., InMemory in tests)
        if (!dbContext.Database.IsRelational()) return;

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "EXEC sp_set_session_context N'org_id', @OrgId";
        cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@OrgId", orgId));

        // SECURITY: If we cannot set the RLS session context we must NOT continue — proceeding
        // without it would allow this unauthenticated webhook code path to read/write rows for
        // ALL organisations. Let the exception propagate so the caller returns 500.
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        logger.LogDebug("WEBHOOK: Tenant context set for OrgId={OrgId}", orgId);
    }
}
