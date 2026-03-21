using Velo.Shared.Models;

namespace Velo.Shared.Contracts;

/// <summary>
/// Metrics repository contract - all operations are automatically scoped to the current org_id.
/// Implementation uses EF Core global query filter + SQL Server RLS for multi-tenant security.
/// </summary>
public interface IMetricsRepository
{
    // DORA Metrics
    Task<DoraMetricsDto?> GetLatestAsync(string orgId, string projectId, CancellationToken cancellationToken);
    Task<IEnumerable<DoraMetricsDto>> GetHistoryAsync(string orgId, string projectId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
    Task SaveAsync(DoraMetricsDto metrics, CancellationToken cancellationToken);

    // Pipeline Runs
    Task<IEnumerable<PipelineRunDto>> GetRunsAsync(string orgId, string projectId, int page, int pageSize, CancellationToken cancellationToken);
    Task<bool> RunExistsAsync(string orgId, string projectId, int adoPipelineId, string runNumber, CancellationToken cancellationToken);
    Task SaveRunAsync(PipelineRunDto run, CancellationToken cancellationToken);

    // Team Health
    Task<TeamHealthDto?> GetTeamHealthAsync(string orgId, string projectId, CancellationToken cancellationToken);
    Task SaveTeamHealthAsync(TeamHealthDto health, CancellationToken cancellationToken);

    // Organization
    Task<OrgContextDto?> GetOrgContextAsync(string orgId, CancellationToken cancellationToken);
    Task SaveOrgContextAsync(OrgContextDto org, CancellationToken cancellationToken);
}

