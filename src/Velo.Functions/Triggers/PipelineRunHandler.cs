using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Velo.Functions.Models;
using Velo.Functions.Services;

namespace Velo.Functions.Triggers;

/// <summary>
/// Processes pipeline completion events after normalization.
/// Handles deployment detection, stage classification, and failure tagging.
/// </summary>
public class PipelineRunHandler(IMetricsEngine metricsEngine, ILogger<PipelineRunHandler> logger)
{
    public async Task HandleAsync(PipelineRunEvent pipelineRun, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Processing pipeline run: PipelineId={PipelineId} Result={Result} OrgId={OrgId}",
            pipelineRun.PipelineId, pipelineRun.Result, pipelineRun.OrgId);

        await metricsEngine.ProcessPipelineRunAsync(pipelineRun, cancellationToken);
    }
}
