namespace Velo.Agent.Tools;

/// <summary>
/// Foundry agent tool: generate structured optimization recommendations.
/// Outputs are cached by pipeline definition hash to control token spend.
/// </summary>
public class RecommendationTool
{
    /// <summary>
    /// Generates prioritized pipeline optimization recommendations.
    /// Results are cached by <paramref name="pipelineHash"/> for the configured TTL.
    /// </summary>
    public Task<IEnumerable<Recommendation>> GenerateAsync(
        string orgId,
        string projectId,
        int pipelineId,
        string pipelineHash,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

public record Recommendation(
    string Title,
    string Description,
    RecommendationPriority Priority,
    string Category,
    string? YamlSnippet = null);

public enum RecommendationPriority { High, Medium, Low }
