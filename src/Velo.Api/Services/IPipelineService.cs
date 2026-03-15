using Velo.Shared.Models;

namespace Velo.Api.Services;

public interface IPipelineService
{
    Task<IEnumerable<PipelineRun>> GetRunsAsync(string projectId, int page, int pageSize, CancellationToken cancellationToken);
    Task<object> GetAnalysisAsync(int pipelineId, CancellationToken cancellationToken);
}

public class PipelineService(Data.VeloDbContext db) : IPipelineService
{
    public Task<IEnumerable<PipelineRun>> GetRunsAsync(string projectId, int page, int pageSize, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<object> GetAnalysisAsync(int pipelineId, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
