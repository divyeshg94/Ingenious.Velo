using Velo.Functions.Models;

namespace Velo.Functions.Services;

public interface IMetricsEngine
{
    Task ComputeAllOrgsAsync(CancellationToken cancellationToken);
    Task ProcessPipelineRunAsync(PipelineRunEvent pipelineRun, CancellationToken cancellationToken);
}

public class MetricsEngine : IMetricsEngine
{
    /// <summary>
    /// Iterates all active orgs and recomputes all five DORA metrics via SQL aggregation.
    /// No AI cost — pure database computation.
    /// </summary>
    public Task ComputeAllOrgsAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException();

    /// <summary>
    /// Handles an individual pipeline run event for immediate metric updates.
    /// </summary>
    public Task ProcessPipelineRunAsync(PipelineRunEvent pipelineRun, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
