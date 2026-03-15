using Velo.Shared.Models;

namespace Velo.Api.Services;

public interface IDoraService
{
    Task<DoraMetrics> GetMetricsAsync(string projectId, int days, CancellationToken cancellationToken);
    Task<IEnumerable<DoraMetrics>> GetHistoryAsync(string projectId, int days, CancellationToken cancellationToken);
    Task<TeamHealth> GetTeamHealthAsync(string projectId, CancellationToken cancellationToken);
}

public class DoraService(Data.VeloDbContext db) : IDoraService
{
    public Task<DoraMetrics> GetMetricsAsync(string projectId, int days, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<IEnumerable<DoraMetrics>> GetHistoryAsync(string projectId, int days, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<TeamHealth> GetTeamHealthAsync(string projectId, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
