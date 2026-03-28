using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Velo.Functions.Models;
using Velo.Shared.Models.Ado;
using Velo.SQL;
using Velo.SQL.Models;

namespace Velo.Functions.Services;

public interface IEventNormalizer
{
    Task NormalizeAndPersistAsync(ServiceHookPayload payload);
}

public class EventNormalizer(VeloDbContext db, ILogger<EventNormalizer> logger) : IEventNormalizer
{
    private const string BuildCompleted = "build.complete";
    private const string PrMerged = "git.pullrequest.merged";
    private const string WorkItemUpdated = "workitem.updated";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public Task NormalizeAndPersistAsync(ServiceHookPayload payload)
    {
        return payload.EventType switch
        {
            BuildCompleted => HandleBuildCompletedAsync(payload),
            PrMerged => HandlePrMergedAsync(payload),
            WorkItemUpdated => HandleWorkItemUpdatedAsync(payload),
            _ => Task.CompletedTask,
        };
    }

    private async Task HandleBuildCompletedAsync(ServiceHookPayload payload)
    {
        var orgId = payload.ResourceContainers?.Collection?.Id;
        if (string.IsNullOrEmpty(orgId) || payload.Resource is null)
        {
            logger.LogWarning("FUNCTIONS: build.complete missing org or resource — skipping");
            return;
        }

        AdoBuildResource? resource;
        try { resource = payload.Resource.Value.Deserialize<AdoBuildResource>(_json); }
        catch (Exception ex)
        {
            logger.LogError(ex, "FUNCTIONS: Failed to deserialize build resource");
            return;
        }

        if (resource is null) return;

        if (!string.Equals(resource.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("FUNCTIONS: Skipping non-completed build status={Status}", resource.Status);
            return;
        }

        var result = resource.Result ?? MapStatusToResult(resource.Status);
        if (string.IsNullOrEmpty(result)) return;

        var projectId = resource.Project?.Name ?? resource.Project?.Id ?? ParseProjectFromUrl(resource.Url);
        if (string.IsNullOrEmpty(projectId))
        {
            logger.LogWarning("FUNCTIONS: Could not resolve projectId for build {BuildId}", resource.Id);
            return;
        }

        var runNumber = resource.BuildNumber ?? resource.Id.ToString();
        var exists = await db.PipelineRuns
            .AsNoTracking()
            .AnyAsync(r => r.OrgId == orgId
                        && r.AdoPipelineId == (resource.Definition != null ? resource.Definition.Id : 0)
                        && r.RunNumber == runNumber);
        if (exists) return;

        var finishTime = resource.FinishTime ?? DateTimeOffset.UtcNow;
        var startTime = resource.StartTime ?? finishTime;

        db.PipelineRuns.Add(new PipelineRun
        {
            OrgId = orgId,
            ProjectId = projectId,
            AdoPipelineId = resource.Definition?.Id ?? 0,
            PipelineName = resource.Definition?.Name ?? string.Empty,
            RunNumber = runNumber,
            Result = result,
            StartTime = startTime,
            FinishTime = finishTime,
            DurationMs = (long)(finishTime - startTime).TotalMilliseconds,
            IsDeployment = IsDeploymentPipeline(resource.Definition?.Name),
            TriggeredBy = resource.RequestedBy?.DisplayName,
            CreatedBy = "functions",
            ModifiedBy = "functions",
        });
        await db.SaveChangesAsync();

        logger.LogInformation(
            "FUNCTIONS: Saved run OrgId={OrgId}, Project={Project}, Pipeline={Pipeline}, Result={Result}",
            orgId, projectId, resource.Definition?.Name, result);
    }

    private async Task HandlePrMergedAsync(ServiceHookPayload payload)
    {
        var orgId = payload.ResourceContainers?.Collection?.Id;
        if (string.IsNullOrEmpty(orgId) || payload.Resource is null)
        {
            logger.LogWarning("FUNCTIONS: git.pullrequest.merged missing org or resource — skipping");
            return;
        }

        AdoPrResource? resource;
        try { resource = payload.Resource.Value.Deserialize<AdoPrResource>(_json); }
        catch (Exception ex)
        {
            logger.LogError(ex, "FUNCTIONS: Failed to deserialize PR resource");
            return;
        }

        if (resource is null) return;

        var projectId = resource.Repository?.Project?.Name ?? resource.Repository?.Project?.Id;
        if (string.IsNullOrEmpty(projectId))
        {
            logger.LogWarning("FUNCTIONS: Could not resolve projectId for PR {PrId}", resource.PullRequestId);
            return;
        }

        var status = resource.Status ?? "completed";
        var exists = await db.PullRequestEvents
            .AnyAsync(p => p.OrgId == orgId && p.PrId == resource.PullRequestId && p.Status == status);
        if (exists) return;

        var isApproved = resource.Reviewers?.Any(r => r.Vote >= 10) ?? false;
        db.PullRequestEvents.Add(new PullRequestEvent
        {
            OrgId = orgId,
            ProjectId = projectId,
            PrId = resource.PullRequestId,
            Title = resource.Title,
            Status = status,
            SourceBranch = resource.SourceRefName,
            TargetBranch = resource.TargetRefName,
            CreatedAt = resource.CreationDate,
            ClosedAt = resource.ClosedDate,
            IsApproved = isApproved,
            ReviewerCount = resource.Reviewers?.Length ?? 0,
            CreatedBy = "functions",
            ModifiedBy = "functions",
        });
        await db.SaveChangesAsync();

        logger.LogInformation(
            "FUNCTIONS: Saved PR {PrId} OrgId={OrgId}, Project={Project}, Approved={Approved}",
            resource.PullRequestId, orgId, projectId, isApproved);
    }

    private async Task HandleWorkItemUpdatedAsync(ServiceHookPayload payload)
    {
        var orgId = payload.ResourceContainers?.Collection?.Id;
        if (string.IsNullOrEmpty(orgId) || payload.Resource is null)
        {
            logger.LogWarning("FUNCTIONS: workitem.updated missing org or resource — skipping");
            return;
        }

        AdoWorkItemResource? resource;
        try { resource = payload.Resource.Value.Deserialize<AdoWorkItemResource>(_json); }
        catch (Exception ex)
        {
            logger.LogError(ex, "FUNCTIONS: Failed to deserialize work item resource");
            return;
        }

        if (resource is null) return;

        // Only persist events where the State actually changed
        var fields = resource.Fields ?? new Dictionary<string, AdoFieldChange>();
        if (!fields.TryGetValue("System.State", out var stateChange) ||
            stateChange.OldValue == stateChange.NewValue)
        {
            logger.LogDebug("FUNCTIONS: workitem.updated has no state change — skipping WI={WI}", resource.WorkItemId);
            return;
        }

        fields.TryGetValue("System.WorkItemType", out var typeChange);
        var workItemType = typeChange?.NewValue ?? typeChange?.OldValue;

        // Resolve project from the workitem's current field values or resource containers
        var projectId = payload.ResourceContainers?.Project?.Id;
        if (resource.WorkItem?.Fields is not null &&
            resource.WorkItem.Fields.TryGetValue("System.TeamProject", out var project))
            projectId = project;

        if (string.IsNullOrEmpty(projectId))
        {
            logger.LogWarning("FUNCTIONS: Could not resolve projectId for work item {WI}", resource.WorkItemId);
            return;
        }

        db.WorkItemEvents.Add(new WorkItemEvent
        {
            OrgId        = orgId,
            ProjectId    = projectId,
            WorkItemId   = resource.WorkItemId,
            WorkItemType = workItemType,
            OldState     = stateChange.OldValue,
            NewState     = stateChange.NewValue,
            ChangedAt    = resource.RevisedDate,
            CreatedBy    = "functions",
            ModifiedBy   = "functions",
        });
        await db.SaveChangesAsync();

        logger.LogInformation(
            "FUNCTIONS: WorkItem state change OrgId={OrgId}, Project={Project}, WI={WI}, {Old}→{New}",
            orgId, projectId, resource.WorkItemId, stateChange.OldValue, stateChange.NewValue);
    }

    private static bool IsDeploymentPipeline(string? name)
    {
        var n = (name ?? string.Empty).ToLowerInvariant();
        return n.Contains("deploy") || n.Contains("release") || n.Contains("prod");
    }

    private static string? ParseProjectFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var parts = url.Split('/');
        if (url.Contains("dev.azure.com"))
        {
            var p = parts.Length >= 5 ? parts[4] : null;
            return string.IsNullOrEmpty(p) || p.StartsWith('_') ? null : p;
        }
        return null;
    }

    private static string? MapStatusToResult(string? status) => status switch
    {
        "succeeded" => "succeeded",
        "failed" => "failed",
        "canceled" => "canceled",
        "partiallySucceeded" => "partiallySucceeded",
        _ => null,
    };
}
