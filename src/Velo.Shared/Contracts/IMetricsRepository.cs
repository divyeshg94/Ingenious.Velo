using Velo.Shared.Models;

namespace Velo.Shared.Contracts;

public interface IMetricsRepository
{
    Task<DoraMetrics?> GetLatestAsync(string orgId, string projectId, CancellationToken cancellationToken);
    Task<IEnumerable<DoraMetrics>> GetHistoryAsync(string orgId, string projectId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
    Task SaveAsync(DoraMetrics metrics, CancellationToken cancellationToken);

    Task<IEnumerable<PipelineRun>> GetRunsAsync(string orgId, string projectId, int page, int pageSize, CancellationToken cancellationToken);
    Task SaveRunAsync(PipelineRun run, CancellationToken cancellationToken);

    Task<TeamHealth?> GetTeamHealthAsync(string orgId, string projectId, CancellationToken cancellationToken);
    Task SaveTeamHealthAsync(TeamHealth health, CancellationToken cancellationToken);
}
