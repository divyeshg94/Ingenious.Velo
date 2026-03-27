using Velo.Shared.Contracts;
using Velo.Shared.Models;

namespace Velo.Api.Services;

public interface ITeamHealthComputeService
{
    Task<TeamHealthDto> ComputeAndSaveAsync(string orgId, string projectId, CancellationToken cancellationToken);
}

/// <summary>
/// Computes Team Health metrics from ingested pipeline run and PR event data.
///
/// Cycle-time derivation:
///   CodingTimeHours    — avg gap between consecutive run starts (coding cadence proxy),
///                        capped at 24 h to exclude weekends / long breaks.
///   ReviewTimeHours    — avg PR lifetime (createdAt→closedAt) for completed PRs.
///                        Falls back to avg CI-run duration when no PR data is available.
///   MergeTimeHours     — same as ReviewTimeHours (PR merge time).
///   DeployTimeHours    — avg duration of deployment-flagged pipeline runs.
///
/// Quality metrics:
///   TestPassRate       — succeeded ÷ (succeeded + failed + partiallySucceeded) × 100.
///   PrApprovalRate     — PRs with at least one approved reviewer ÷ all closed PRs × 100.
///                        Falls back to CI pass rate when no PR data is available.
///   FlakyTestRate      — pipelines with ≥30 % result-flip rate ÷ all multi-run pipelines × 100.
///   DeploymentRiskScore— composite: failRate×0.5 + flakyRate×0.3 + deployDurationFactor×0.2.
///
/// Phase 2:
///   AveragePrSizeLines — stored as 0 until PR diff data is available.
///   PrCommentDensity   — stored as 0 until comment counts are ingested.
/// </summary>
public class TeamHealthComputeService(
    IMetricsRepository repo,
    ILogger<TeamHealthComputeService> logger) : ITeamHealthComputeService
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

        var to   = DateTimeOffset.UtcNow;
        var from = to.AddDays(-PeriodDays);

        // Fetch up to 500 most recent runs; filter to the rolling 30-day window.
        var allRuns  = (await repo.GetRunsAsync(orgId, projectId, 1, 500, cancellationToken)).ToList();
        var runs     = allRuns.Where(r => r.StartTime >= from).ToList();

        // Fetch PR events for the same 30-day window.
        var prEvents  = (await repo.GetPrEventsAsync(orgId, projectId, from, cancellationToken)).ToList();
        var hasPrData = prEvents.Count > 0;

        logger.LogInformation(
            "HEALTH: Found {Total} total runs, {Period} in last {Days} days, {PrCount} PR events — OrgId={OrgId}",
            allRuns.Count, runs.Count, PeriodDays, prEvents.Count, orgId);

        // ── Cycle time metrics ────────────────────────────────────────────
        var codingTimeHours = ComputeCodingTime(runs);
        var reviewTimeHours = hasPrData ? ComputePrReviewTime(prEvents) : ComputeReviewTime(runs);
        var mergeTimeHours  = hasPrData ? reviewTimeHours               : ComputeMergeTime(runs);
        var deployTimeHours = ComputeDeployTime(runs);

        // ── Quality metrics ───────────────────────────────────────────────
        var testPassRate    = ComputeTestPassRate(runs);
        var prApprovalRate  = hasPrData ? ComputePrApprovalRate(prEvents) : testPassRate;
        var flakyTestRate   = ComputeFlakyTestRate(runs);
        var deployRiskScore = ComputeDeploymentRiskScore(runs, flakyTestRate);

        var health = new TeamHealthDto
        {
            Id                  = Guid.NewGuid(),
            OrgId               = orgId,
            ProjectId           = projectId,
            ComputedAt          = DateTimeOffset.UtcNow,
            CodingTimeHours     = Round(codingTimeHours),
            ReviewTimeHours     = Round(reviewTimeHours),
            MergeTimeHours      = Round(mergeTimeHours),
            DeployTimeHours     = Round(deployTimeHours),
            AveragePrSizeLines  = 0,          // Phase 2
            PrCommentDensity    = 0,          // Phase 2
            PrApprovalRate      = Round(prApprovalRate,  1),
            TestPassRate        = Round(testPassRate,     1),
            FlakyTestRate       = Round(flakyTestRate,   1),
            DeploymentRiskScore = Round(deployRiskScore, 1),
        };

        await repo.SaveTeamHealthAsync(health, cancellationToken);

        logger.LogInformation(
            "HEALTH: Saved — OrgId={OrgId}, ProjectId={ProjectId}, " +
            "ReviewTimeH={ReviewH:F1} ({Source}), PrApproval={Approval:F1}%, " +
            "TestPassRate={TestPassRate:F1}, FlakyRate={FlakyRate:F1}, RiskScore={Risk:F1}",
            orgId, projectId,
            health.ReviewTimeHours, hasPrData ? "PR data" : "CI proxy",
            health.PrApprovalRate,
            health.TestPassRate, health.FlakyTestRate, health.DeploymentRiskScore);

        return health;
    }

    // ── PR-based cycle time ────────────────────────────────────────────────────

    /// <summary>
    /// Average PR lifetime (creationDate → closedDate) for completed/abandoned PRs.
    /// Excludes outliers > 30 days to prevent stale drafts from skewing the metric.
    /// </summary>
    private static double ComputePrReviewTime(List<PullRequestEventDto> prEvents)
    {
        var closed = prEvents
            .Where(p => p.Status is "completed" or "abandoned" && p.ClosedAt.HasValue)
            .Select(p => (p.ClosedAt!.Value - p.CreatedAt).TotalHours)
            .Where(h => h > 0 && h <= 30 * 24)   // cap at 30 days
            .ToList();

        return closed.Any() ? closed.Average() : 0;
    }

    // ── PR approval rate ──────────────────────────────────────────────────────

    /// <summary>
    /// PRs marked as approved (at least one reviewer vote >= 10) ÷ all closed PRs × 100.
    /// </summary>
    private static double ComputePrApprovalRate(List<PullRequestEventDto> prEvents)
    {
        var closed = prEvents
            .Where(p => p.Status is "completed" or "abandoned")
            .ToList();

        if (closed.Count == 0) return 0;
        var approved = closed.Count(p => p.IsApproved);
        return (double)approved / closed.Count * 100;
    }

    // ── Coding time ───────────────────────────────────────────────────────────
    /// Average gap between consecutive run starts, ignoring gaps > 24 h
    /// (weekends / holidays skew this otherwise).
    private static double ComputeCodingTime(List<PipelineRunDto> runs)
    {
        if (runs.Count < 2) return 0;
        var sorted   = runs.OrderBy(r => r.StartTime).ToList();
        var workGaps = sorted
            .Zip(sorted.Skip(1), (a, b) => (b.StartTime - a.StartTime).TotalHours)
            .Where(g => g > 0 && g <= 24)
            .ToList();
        return workGaps.Any() ? workGaps.Average() : 0;
    }

    // ── Review time (pipeline proxy) ──────────────────────────────────────────
    /// Average duration of non-deployment (CI / PR-gate) pipeline runs.
    private static double ComputeReviewTime(List<PipelineRunDto> runs)
    {
        var ciRuns = runs
            .Where(r => !r.IsDeployment && r.DurationMs.HasValue && r.DurationMs > 0)
            .ToList();
        return ciRuns.Any() ? ciRuns.Average(r => r.DurationMs!.Value / 3_600_000.0) : 0;
    }

    // ── Merge time (pipeline proxy) ───────────────────────────────────────────
    /// Average total pipeline duration across all completed runs.
    private static double ComputeMergeTime(List<PipelineRunDto> runs)
    {
        var completed = runs.Where(r => r.DurationMs.HasValue && r.DurationMs > 0).ToList();
        return completed.Any() ? completed.Average(r => r.DurationMs!.Value / 3_600_000.0) : 0;
    }

    // ── Deploy time ───────────────────────────────────────────────────────────
    /// Average duration of deployment-flagged pipeline runs.
    private static double ComputeDeployTime(List<PipelineRunDto> runs)
    {
        var deploys = runs
            .Where(r => r.IsDeployment && r.DurationMs.HasValue && r.DurationMs > 0)
            .ToList();
        return deploys.Any() ? deploys.Average(r => r.DurationMs!.Value / 3_600_000.0) : 0;
    }

    // ── Test pass rate ────────────────────────────────────────────────────────
    /// succeeded ÷ (succeeded + failed + partiallySucceeded) × 100
    private static double ComputeTestPassRate(List<PipelineRunDto> runs)
    {
        var concluded = runs
            .Where(r => r.Result is "succeeded" or "failed" or "partiallySucceeded")
            .ToList();
        if (concluded.Count == 0) return 0;
        var passed = concluded.Count(r => r.Result == "succeeded");
        return (double)passed / concluded.Count * 100;
    }

    // ── Flaky test rate ───────────────────────────────────────────────────────
    /// Proportion of pipelines (with ≥ 3 runs) that alternate result ≥ 30 % of the time.
    private static double ComputeFlakyTestRate(List<PipelineRunDto> runs)
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
            var flips   = results.Zip(results.Skip(1), (a, b) => a != b ? 1 : 0).Sum();
            return (double)flips / (results.Count - 1) >= 0.30;
        });

        return (double)flakyCount / byPipeline.Count * 100;
    }

    // ── Deployment risk score ─────────────────────────────────────────────────
    /// Composite 0–100 score: higher = more risk.
    /// failRate × 0.5 + flakyRate × 0.3 + deployDurationFactor × 0.2
    private static double ComputeDeploymentRiskScore(
        List<PipelineRunDto> runs,
        double flakyRate)
    {
        var concluded  = runs.Where(r => r.Result is "succeeded" or "failed" or "partiallySucceeded").ToList();
        var failRate   = concluded.Count > 0
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
