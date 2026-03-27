namespace Velo.Agent.Tools;

/// <summary>
/// Foundry agent tool: generate prioritized optimization recommendations from DORA data.
/// Uses <see cref="IAgentDataProvider"/> to keep Velo.Agent free of EF Core dependencies.
/// </summary>
public class RecommendationTool(IAgentDataProvider dataProvider)
{
    /// <summary>Returns a recommendation context string built from DORA metric ratings.</summary>
    public Task<string> GetDoraRecommendationsAsync(string orgId, string projectId, CancellationToken cancellationToken = default)
        => dataProvider.GetDoraContextAsync(orgId, projectId, cancellationToken);
}

public record Recommendation(
    string Title,
    string Description,
    RecommendationPriority Priority,
    string Category,
    string? YamlSnippet = null);

public enum RecommendationPriority { High, Medium, Low }
