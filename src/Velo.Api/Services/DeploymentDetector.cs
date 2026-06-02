namespace Velo.Api.Services;

/// <summary>
/// Shared heuristic for tagging a pipeline as a "deployment" pipeline.
///
/// A pipeline is considered a deployment when its name OR any stage name contains
/// one of the keywords below. The list is deliberately wider than the original
/// (deploy / release / prod) trio to catch the common naming patterns used across
/// Azure DevOps customers:
///
///   • deploy        — "Deploy to UAT", "deploy-prod"
///   • release       — "Release Train", "ci-release"
///   • prod / production — "Prod Push", "production-rollout"
///   • publish       — "Publish Package", "publish-website"
///   • rollout       — "Canary Rollout"
///   • canary        — "Canary Deploy"
///   • cd            — "API-CD" (continuous deployment marker)
///   • promote       — "Promote to staging"
///
/// Lower-cased, invariant-culture matching so case differences don't matter.
/// This is intentionally permissive — once a customer connects, every metric that
/// depends on "deployments" has a meaningful denominator. Customers who want
/// exact tagging can use the Velo@1 pipeline task to set StageName explicitly,
/// which short-circuits the heuristic.
/// </summary>
internal static class DeploymentDetector
{
    private static readonly string[] Keywords =
    [
        "deploy",
        "release",
        "prod",
        "production",
        "publish",
        "rollout",
        "canary",
        "promote",
    ];

    /// <summary>
    /// True when the pipeline name or stage name matches a deployment keyword.
    /// Either argument may be null/empty.
    /// </summary>
    public static bool IsDeployment(string? pipelineName, string? stageName = null)
    {
        if (Matches(pipelineName)) return true;
        if (Matches(stageName)) return true;
        return false;
    }

    private static bool Matches(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var lowered = value.ToLowerInvariant();

        // Whole-word "cd" check — avoid matching "code" or "ascend"
        if (ContainsWholeWord(lowered, "cd")) return true;

        foreach (var keyword in Keywords)
            if (lowered.Contains(keyword)) return true;

        return false;
    }

    private static bool ContainsWholeWord(string haystack, string needle)
    {
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            var before = idx == 0 || !char.IsLetterOrDigit(haystack[idx - 1]);
            var afterIdx = idx + needle.Length;
            var after = afterIdx >= haystack.Length || !char.IsLetterOrDigit(haystack[afterIdx]);
            if (before && after) return true;
            idx = afterIdx;
        }
        return false;
    }
}
