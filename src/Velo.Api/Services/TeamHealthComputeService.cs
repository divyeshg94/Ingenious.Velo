using Velo.Shared.Contracts;
using Velo.Shared.Models;

namespace Velo.Api.Services;

/// <summary>
/// Computes Team Health metrics from ingested pipeline run data.
///
/// Metrics derivation strategy (Phase 1 — pipeline data only):
///   CodingTimeHours    — avg gap between consecutive run starts (coding cadence proxy),
///                        capped at 24 h to exclude weekends / long breaks.
///   ReviewTimeHours    — avg duration of non-deployment (CI / PR gate) pipeline runs.
///   MergeTimeHours     — avg duration across all completed runs (full pipeline latency).
///   DeployTimeHours    — avg duration of deployment-flagged pipeline runs.
///   TestPassRate       — succeeded ÷ (succeeded + failed + partiallySucceeded) × 100.
///   PrApprovalRate     — CI gate pass rate used as proxy; replaces real PR approval data.
///   FlakyTestRate      — pipelines with ≥30 % result-flip rate ÷ all multi-run pipelines × 100.
///   DeploymentRiskScore— composite: failRate×0.5 + flakyRate×0.3 + deployDurationFactor×0.2.
///
/// Phase 2 (requires GitHub / ADO PR API):
///   AveragePrSizeLines — not computable from pipeline data; stored as 0.
///   PrCommentDensity   — not computable from pipeline data; stored as 0.
/// </summary>
public class TeamHealthComputeService(
    IMetricsRepository repo,
    ILogger<TeamHealthComputeService> logger)
{
    private const int PeriodDays = 30;

    public async Task<TeamHealthDto> ComputeAndSaveAsync(
        string orgId,
        string projectId,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "HEALTH: Computing team health — OrgId={OrgId}, ProjectId={ProjectId}",
            orgId, projectId);

        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-PeriodDays);

        // Fetch up to 500 most recent runs; filter to the rolling 30-day window.
        var allRuns = (await repo.GetRunsAsync(orgId, projectId, 1, 500, cancellationToken)).ToList();
        var runs = allRuns.Where(r => r.StartTime >= from).ToList();

        logger.LogInformation(
            "HEALTH: Found {Total} total runs, {Period} in last {Days} days — OrgId={OrgId}",
            allRuns.Count, runs.Count, PeriodDays, orgId);

        // ── Cycle time metrics ────────────────────────────────────────────
        var codingTimeHours   = ComputeCodingTime(runs);
        var reviewTimeHours   = ComputeReviewTime(runs);
        var mergeTimeHours    = ComputeMergeTime(runs);
        var deployTimeHours   = ComputeDeployTime(runs);

        // ── Quality metrics ───────────────────────────────────────────────
        var testPassRate      = ComputeTestPassRate(runs);
        var prApprovalRate    = testPassRate;   // CI pass rate is the best proxy we have
        var flakyTestRate     = ComputeFlakyTestRate(runs);
        var deployRiskScore   = ComputeDeploymentRiskScore(runs, flakyTestRate);

        var health = new TeamHealthDto
        {
            Id              = Guid.NewGuid(),
            OrgId           = orgId,
            ProjectId       = projectId,
            ComputedAt      = DateTimeOffset.UtcNow,
            CodingTimeHours    = Round(codingTimeHours),
            ReviewTimeHours    = Round(reviewTimeHours),
            MergeTimeHours     = Round(mergeTimeHours),
            DeployTimeHours    = Round(deployTimeHours),
            AveragePrSizeLines = 0,          // Phase 2
            PrCommentDensity   = 0,          // Phase 2
            PrApprovalRate     = Round(prApprovalRate, 1),
            TestPassRate       = Round(testPassRate,   1),
            FlakyTestRate      = Round(flakyTestRate,  1),
            DeploymentRiskScore= Round(deployRiskScore,1),
        };

        await repo.SaveTeamHealthAsync(health, cancellationToken);

        logger.LogInformation(
            "HEALTH: Saved team health — OrgId={OrgId}, ProjectId={ProjectId}, " +
            "TestPassRate={TestPassRate:F1}, FlakyRate={FlakyRate:F1}, RiskScore={Risk:F1}",
            orgId, projectId, health.TestPassRate, health.FlakyTestRate, health.DeploymentRiskScore);

        return health;
    }

    // ── Coding time ───────────────────────────────────────────────────────
    /// Average gap between consecutive run starts, ignoring gaps > 24 h
    /// (weekends / holidays skew this otherwise).
    private static double ComputeCodingTime(List<Velo.Shared.Models.PipelineRunDto> runs)
    {
        if (runs.Count < 2) return 0;
        var sorted = runs.OrderBy(r => r.StartTime).ToList();
        var workGaps = sorted
            .Zip(sorted.Skip(1), (a, b) => (b.StartTime - a.StartTime).TotalHours)
            .Where(g => g > 0 && g <= 24)
            .ToList();
        return workGaps.Any() ? workGaps.Average() : 0;
    }

    // ── Review time ───────────────────────────────────────────────────────
    /// Average duration of non-deployment (CI / PR-gate) pipeline runs.
    private static double ComputeReviewTime(List<Velo.Shared.Models.PipelineRunDto> runs)
    {
        var ciRuns = runs
            .Where(r => !r.IsDeployment && r.DurationMs.HasValue && r.DurationMs > 0)
            .ToList();
        return ciRuns.Any() ? ciRuns.Average(r => r.DurationMs!.Value / 3_600_000.0) : 0;
    }

    // ── Merge time ────────────────────────────────────────────────────────
    /// Average total pipeline duration across all completed runs.
    private static double ComputeMergeTime(List<Velo.Shared.Models.PipelineRunDto> runs)
    {
        var completed = runs.Where(r => r.DurationMs.HasValue && r.DurationMs > 0).ToList();
        return completed.Any() ? completed.Average(r => r.DurationMs!.Value / 3_600_000.0) : 0;
    }

    // ── Deploy time ───────────────────────────────────────────────────────
    /// Average duration of deployment-flagged pipeline runs.
    private static double ComputeDeployTime(List<Velo.Shared.Models.PipelineRunDto> runs)
    {
        var deploys = runs
            .Where(r => r.IsDeployment && r.DurationMs.HasValue && r.DurationMs > 0)
            .ToList();
        return deploys.Any() ? deploys.Average(r => r.DurationMs!.Value / 3_600_000.0) : 0;
    }

    // ── Test pass rate ────────────────────────────────────────────────────
    /// succeeded ÷ (succeeded + failed + partiallySucceeded) × 100
    private static double ComputeTestPassRate(List<Velo.Shared.Models.PipelineRunDto> runs)
    {
        var concluded = runs
            .Where(r => r.Result is "succeeded" or "failed" or "partiallySucceeded")
            .ToList();
        if (concluded.Count == 0) return 0;
        var passed = concluded.Count(r => r.Result == "succeeded");
        return (double)passed / concluded.Count * 100;
    }

    // ── Flaky test rate ───────────────────────────────────────────────────
    /// Proportion of pipelines (with ≥ 3 runs) that alternate result ≥ 30 % of the time.
    private static double ComputeFlakyTestRate(List<Velo.Shared.Models.PipelineRunDto> runs)
    {
        var byPipeline = runs
            .Where(r => r.Result is "succeeded" or "failed")
            .GroupBy(r => r.AdoPipelineId)
            .Where(g => g.Count() >= 3)
            .ToList();

        if (byPipeline.Count == 0) return 0;

        var flakyCount = byPipeline.Count(group =>
        {
            var results = group.OrderBy(r => r.StartTime).Select(r => r.Result).ToList();
            var flips = results.Zip(results.Skip(1), (a, b) => a != b ? 1 : 0).Sum();
            return (double)flips / (results.Count - 1) >= 0.30;
        });

        return (double)flakyCount / byPipeline.Count * 100;
    }

    // ── Deployment risk score ─────────────────────────────────────────────
    /// Composite 0–100 score: higher = more risk.
    /// failRate × 0.5 + flakyRate × 0.3 + deployDurationFactor × 0.2
    private static double ComputeDeploymentRiskScore(
        List<Velo.Shared.Models.PipelineRunDto> runs,
        double flakyRate)
    {
        var concluded = runs.Where(r => r.Result is "succeeded" or "failed" or "partiallySucceeded").ToList();
        var failRate = concluded.Count > 0
            ? (double)concluded.Count(r => r.Result == "failed") / concluded.Count * 100
            : 0;

        var avgDeployHours = ComputeDeployTime(runs);
        // Normalise: deploys under 30 min score 0; over 2 h score 100.
        var durationFactor = avgDeployHours <= 0.5 ? 0
            : Math.Min((avgDeployHours - 0.5) / 1.5 * 100, 100);

        var score = failRate * 0.5 + flakyRate * 0.3 + durationFactor * 0.2;
        return Math.Min(score, 100);
    }

    private static double Round(double v, int decimals = 2) => Math.Round(v, decimals);
}
