namespace Velo.Agent.Tools;

/// <summary>
/// Foundry agent tool: parse YAML pipeline definitions and query build history.
/// Used by the agent to identify bottlenecks and predict failure patterns.
/// </summary>
public class PipelineAnalysisTool
{
    /// <summary>Fetches and parses the YAML definition for a given pipeline ID.</summary>
    public Task<string> GetPipelineYamlAsync(string orgId, string projectId, int pipelineId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    /// <summary>Returns the last N build results with durations and failure reasons.</summary>
    public Task<object> GetBuildHistoryAsync(string orgId, string projectId, int pipelineId, int count = 20, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>Identifies the slowest stages across recent runs.</summary>
    public Task<object> IdentifyBottlenecksAsync(string orgId, string projectId, int pipelineId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
