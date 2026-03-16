using Velo.Shared.Models;

namespace Velo.Shared.Contracts;

public interface IMetricsRepository
{
    Task<DoraMetricsDto?> GetLatestAsync(string orgId, string projectId, CancellationToken cancellationToken);
    Task<IEnumerable<DoraMetricsDto>> GetHistoryAsync(string orgId, string projectId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
    Task SaveAsync(DoraMetricsDto metrics, CancellationToken cancellationToken);

    Task<IEnumerable<PipelineRunDto>> GetRunsAsync(string orgId, string projectId, int page, int pageSize, CancellationToken cancellationToken);
    Task SaveRunAsync(PipelineRunDto run, CancellationToken cancellationToken);

    Task<TeamHealthDto?> GetTeamHealthAsync(string orgId, string projectId, CancellationToken cancellationToken);
    Task SaveTeamHealthAsync(TeamHealthDto health, CancellationToken cancellationToken);
}
