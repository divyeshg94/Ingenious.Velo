using Velo.Shared.Models;

namespace Velo.Shared.Contracts;

/// <summary>
/// Metrics repository contract - all operations are automatically scoped to the current org_id.
/// Implementation uses EF Core global query filter + SQL Server RLS for multi-tenant security.
/// </summary>
public interface IMetricsRepository
{
    // DORA Metrics
    Task<DoraMetricsDto?> GetLatestAsync(string orgId, string projectId, string? repositoryName, CancellationToken cancellationToken);
    Task<IEnumerable<DoraMetricsDto>> GetHistoryAsync(string orgId, string projectId, DateTimeOffset from, DateTimeOffset to, string? repositoryName, CancellationToken cancellationToken);
    Task SaveAsync(DoraMetricsDto metrics, CancellationToken cancellationToken);

    // Pipeline Runs
    Task<IEnumerable<PipelineRunDto>> GetRunsAsync(string orgId, string projectId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every pipeline run for the given org/project whose StartTime falls
    /// within [from, to). Unlike <see cref="GetRunsAsync"/> there is no page cap —
    /// callers computing rolling-window metrics must see every run in the window
    /// or the result is silently wrong.
    ///
    /// When <paramref name="repositoryNames"/> is non-empty the result is restricted
    /// to runs whose RepositoryName is one of the supplied values. Null/empty means
    /// "all repositories" (project-wide aggregate).
    /// </summary>
    Task<IEnumerable<PipelineRunDto>> GetRunsInPeriodAsync(
        string orgId, string projectId, DateTimeOffset from, DateTimeOffset to,
        IReadOnlyCollection<string>? repositoryNames, CancellationToken cancellationToken);

    Task<bool> RunExistsAsync(string orgId, string projectId, int adoPipelineId, string runNumber, CancellationToken cancellationToken);
    Task SaveRunAsync(PipelineRunDto run, CancellationToken cancellationToken);

    /// <summary>
    /// Updates StageName + IsDeployment on an existing pipeline run.
    /// Used by the webhook background path that fetches the build timeline AFTER
    /// the run has been persisted (the webhook returns immediately, the timeline
    /// fetch happens fire-and-forget).
    /// </summary>
    Task UpdateRunStageAsync(string orgId, string projectId, Guid runId, string? stageName, bool isDeployment, CancellationToken cancellationToken);

    // Team Health
    Task<TeamHealthDto?> GetTeamHealthAsync(string orgId, string projectId, string? repositoryName, CancellationToken cancellationToken);
    Task SaveTeamHealthAsync(TeamHealthDto health, CancellationToken cancellationToken);

    // Pull Requests
    Task SavePrEventAsync(PullRequestEventDto pr, CancellationToken cancellationToken);
    Task<bool> PrEventExistsAsync(string orgId, string projectId, int prId, string status, CancellationToken cancellationToken);
    Task<IEnumerable<PullRequestEventDto>> GetPrEventsAsync(string orgId, string projectId, DateTimeOffset from, CancellationToken cancellationToken);

    // Organization
    Task<OrgContextDto?> GetOrgContextAsync(string orgId, CancellationToken cancellationToken);
    Task SaveOrgContextAsync(OrgContextDto org, CancellationToken cancellationToken);

    // Team Mappings
    Task<IEnumerable<TeamMappingDto>> GetTeamMappingsAsync(string orgId, string projectId, CancellationToken cancellationToken);
    Task<TeamMappingDto?> GetTeamMappingAsync(string orgId, string projectId, string repositoryName, CancellationToken cancellationToken);
    Task SaveTeamMappingAsync(TeamMappingDto mapping, CancellationToken cancellationToken);
    Task DeleteTeamMappingAsync(Guid id, string orgId, CancellationToken cancellationToken);

    // Work Item Events
    Task SaveWorkItemEventAsync(WorkItemEventDto item, CancellationToken cancellationToken);
    Task<IEnumerable<WorkItemEventDto>> GetWorkItemEventsAsync(string orgId, string projectId, DateTimeOffset from, CancellationToken cancellationToken);

    // Repository discovery
    Task<IEnumerable<string>> GetDistinctRepositoriesAsync(string orgId, string projectId, CancellationToken cancellationToken);
    Task<IEnumerable<int>> GetPipelineIdsWithNullRepositoryAsync(string orgId, string projectId, CancellationToken cancellationToken);
    Task BackfillRepositoryNameAsync(string orgId, string projectId, int adoPipelineId, string repositoryName, CancellationToken cancellationToken);
}

