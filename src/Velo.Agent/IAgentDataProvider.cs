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
}
