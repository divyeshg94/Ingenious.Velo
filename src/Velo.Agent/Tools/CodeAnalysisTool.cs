namespace Velo.Agent.Tools;

/// <summary>
/// Foundry agent tool: analyze PR size, test coverage trends, and code churn.
/// Provides data for rework rate computation and change failure risk scoring.
/// </summary>
public class CodeAnalysisTool
{
    /// <summary>Returns PR size metrics (files changed, lines added/removed) for recent PRs.</summary>
    public Task<object> GetPrSizeMetricsAsync(string orgId, string projectId, int days = 30, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>Returns test pass rate and flakiness trends over time.</summary>
    public Task<object> GetTestStabilityTrendsAsync(string orgId, string projectId, int pipelineId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>Computes code churn rate — indicator of rework and technical debt.</summary>
    public Task<double> GetCodeChurnRateAsync(string orgId, string projectId, int days = 30, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
