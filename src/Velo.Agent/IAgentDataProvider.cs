namespace Velo.Agent;

/// <summary>
/// Provides contextual data strings for the Foundry agent.
/// Implemented in Velo.Api using EF Core so that Velo.Agent stays free of DB dependencies.
/// </summary>
public interface IAgentDataProvider
{
    /// <summary>Returns a formatted summary of recent pipeline run history for the project.</summary>
    Task<string> GetPipelineContextAsync(string orgId, string projectId, CancellationToken ct = default);

    /// <summary>Returns a formatted summary of current DORA metric scores and ratings.</summary>
    Task<string> GetDoraContextAsync(string orgId, string projectId, CancellationToken ct = default);

    /// <summary>Returns a formatted summary of recent pull request events.</summary>
    Task<string> GetPrContextAsync(string orgId, string projectId, CancellationToken ct = default);

    /// <summary>
    /// Persists the Foundry agent ID that was auto-created by VeloAgent on first use.
    /// Subsequent calls will use this ID instead of creating a new agent.
    /// </summary>
    Task SaveAgentIdAsync(string orgId, string agentId, CancellationToken ct = default);
}
