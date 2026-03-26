namespace Velo.Agent.Tools;

/// <summary>
/// Foundry agent tool: surface PR metrics and code quality signals from the Velo DB.
/// Uses <see cref="IAgentDataProvider"/> to keep Velo.Agent free of EF Core dependencies.
/// </summary>
public class CodeAnalysisTool(IAgentDataProvider dataProvider)
{
    /// <summary>Returns PR size and review metrics as a formatted context string.</summary>
    public Task<string> GetPrSizeMetricsAsync(string orgId, string projectId, CancellationToken cancellationToken = default)
        => dataProvider.GetPrContextAsync(orgId, projectId, cancellationToken);
}
