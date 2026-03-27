namespace Velo.Agent.Tools;

/// <summary>
/// Foundry agent tool: surface pipeline history and bottleneck data from the Velo DB.
/// Uses <see cref="IAgentDataProvider"/> to keep Velo.Agent free of EF Core dependencies.
/// </summary>
public class PipelineAnalysisTool(IAgentDataProvider dataProvider)
{
    /// <summary>Returns the last N pipeline runs as a formatted context string.</summary>
    public Task<string> GetBuildHistoryAsync(string orgId, string projectId, CancellationToken cancellationToken = default)
        => dataProvider.GetPipelineContextAsync(orgId, projectId, cancellationToken);

    /// <summary>Builds a bottleneck analysis prompt from recent run durations.</summary>
    public async Task<string> IdentifyBottlenecksAsync(string orgId, string projectId, CancellationToken cancellationToken = default)
    {
        var history = await dataProvider.GetPipelineContextAsync(orgId, projectId, cancellationToken);
        return $"Pipeline build history for bottleneck analysis:\n{history}";
    }
}
