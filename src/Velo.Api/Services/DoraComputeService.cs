using Velo.Shared.Contracts;
using Velo.Shared.Models;

namespace Velo.Api.Services;

public interface IDoraComputeService
{
    Task<DoraMetricsDto> ComputeAndSaveAsync(string orgId, string projectId, CancellationToken cancellationToken);
}

/// <summary>
/// Computes DORA metrics from stored pipeline runs and work-item events, then saves them.
/// Called after each ingestion so the dashboard always has fresh metrics.
///
/// Metric notes (aligned with DORA 2024 benchmarks — https://dora.dev/guides/dora-metrics/):
///
///   Deployment Frequency — successful deployment-tagged runs ÷ 30 days.
///     Fallback: all successful runs when no pipelines are tagged as deployments
///     (IsDeploymentFrequencyEstimated = true in that case).
///     Benchmarks: Elite ≥1/day, High ≥1/week, Medium ≥1/month, Low &lt;1/month.
///
///   Lead Time for Changes — average pipeline build duration of successful runs (proxy).
///     IsLeadTimeApproximate is always true; true commit-to-production time requires
///     PR event linkage which is not yet implemented.
///     Benchmarks: Elite ≤1h, High ≤1day, Medium ≤1week, Low &gt;1week.
///
///   Change Failure Rate — percentage of deployments that cause a production failure
///     (require hotfix, rollback, or remedial action).
///     Uses deployment-tagged failed runs ÷ total deployment runs × 100.
///     Fallback: all runs when none are tagged (IsChangeFailureRateEstimated = true).
///     Benchmarks: Elite ≤15%, High ≤30%, Medium ≤45%, Low &gt;45%.
///
///   Mean Time to Restore (Time to Restore Service) — average time from a failed
///     deployment run to the next successful run of the same pipeline.
///     Uses deployment-tagged runs only; falls back to all runs when none are tagged
///     (IsMttrEstimated = true in that case).
///     Benchmarks: Elite ≤1h, High ≤1day, Medium ≤1week, Low &gt;1week.
///
///   Rework Rate — measures the ratio of unplanned work (hotfixes/rollbacks) to total
///     completions, aligned with the DORA standard: Unplanned Deployments ÷ Total Deployments.
///     Proxied here via work-item state-transition churn: transitions from a done state
///     back to an active state ÷ total completions × 100 (via WorkItemReworkCalculator).
///     Benchmarks: Elite ≤4%, High ≤8%, Medium ≤32%, Low &gt;32%.
///     IsReworkRateEstimated = true when no work-item events were available for the period.
/// </summary>
public class DoraComputeService(
    IMetricsRepository repo,
    ILogger<DoraComputeService> logger) : IDoraComputeService
{
    private const int PeriodDays = 30;

    public async Task<DoraMetricsDto> ComputeAndSaveAsync(
        string orgId,
        string projectId,
        CancellationToken cancellationToken)
    {
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-PeriodDays);

        // Period-based query — no page cap. Busy customers (hundreds of runs/day)
        // were silently truncated to the most recent 500 runs which biased every
        // metric toward "Elite" as older failures fell out of the window.
        var periodRuns = (await repo.GetRunsInPeriodAsync(orgId, projectId, from, to, cancellationToken))
            .ToList();

        var workItemEvents = (await repo.GetWorkItemEventsAsync(orgId, projectId, from, cancellationToken))
            .ToList();

        // PR events drive real Lead Time computation when available. The repo
        // projection now carries diff + cycle data; when absent we fall back to
        // a build-duration proxy and flag the result as approximate.
        var prEvents = (await repo.GetPrEventsAsync(orgId, projectId, from, cancellationToken))
            .ToList();

        logger.LogInformation(
            "DORA_COMPUTE: {Period} runs in last {Days} days, {WiCount} WI events, {PrCount} PR events — OrgId={OrgId}, ProjectId={ProjectId}",
            periodRuns.Count, PeriodDays, workItemEvents.Count, prEvents.Count,
            SanitizeForLog(orgId), SanitizeForLog(projectId));

        var metrics = new DoraMetricsDto
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ProjectId = projectId,
            ComputedAt = DateTimeOffset.UtcNow,
            PeriodStart = from,
            PeriodEnd = to,
        };

        // ── Deployment Frequency ─────────────────────────────────────────────────
        var deployments = periodRuns
            .Where(r => r.IsDeployment && r.Result.Equals("succeeded", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var hasDeploymentTaggedPipelines = deployments.Any() ||
            periodRuns.Any(r => r.IsDeployment);

        // Fallback: use all successful runs when no pipelines are tagged as deployments.
        var successfulRuns = periodRuns
            .Where(r => r.Result.Equals("succeeded", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var usingFallback = !hasDeploymentTaggedPipelines;
        var deploymentsForFreq = usingFallback ? successfulRuns : deployments;
        metrics.DeploymentFrequency = deploymentsForFreq.Count / (double)PeriodDays;
        metrics.DeploymentFrequencyRating = RateDeploymentFrequency(metrics.DeploymentFrequency);
        metrics.IsDeploymentFrequencyEstimated = usingFallback;

        // ── Lead Time for Changes ────────────────────────────────────────────────
        // DORA: average time from a code change being committed to running in production.
        // Best signal: PR merge time → next successful deployment finish time.
        // Fallback: average successful build duration (clearly flagged as approximate).
        var leadTime = ComputeLeadTimeFromPrAndDeploys(prEvents, deployments);

        if (leadTime is { } realLeadTimeHours)
        {
            metrics.LeadTimeForChangesHours = realLeadTimeHours;
            metrics.IsLeadTimeApproximate = false;
        }
        else
        {
            // Proxy: avg successful run duration. Always marked approximate.
            var completedRuns = periodRuns
                .Where(r => r.DurationMs.HasValue && r.Result.Equals("succeeded", StringComparison.OrdinalIgnoreCase))
                .ToList();

            metrics.LeadTimeForChangesHours = completedRuns.Any()
                ? completedRuns.Average(r => r.DurationMs!.Value) / 3_600_000.0
                : 0;
            metrics.IsLeadTimeApproximate = true;
        }
        metrics.LeadTimeRating = RateLeadTime(metrics.LeadTimeForChangesHours);

        // ── Change Failure Rate ───────────────────────────────────────────────────
        // DORA defines CFR as deployments causing production failures ÷ total deployments.
        // Use deployment-tagged runs only; fallback to all runs when none are tagged.
        var allDeploymentRuns = periodRuns.Where(r => r.IsDeployment).ToList();
        var cfrUsingFallback = !allDeploymentRuns.Any();
        var runsForCfr = cfrUsingFallback ? periodRuns : allDeploymentRuns;
        var failedForCfr = runsForCfr.Count(r => r.Result.Equals("failed", StringComparison.OrdinalIgnoreCase));

        metrics.ChangeFailureRate = runsForCfr.Any()
            ? failedForCfr / (double)runsForCfr.Count * 100.0
            : 0;
        metrics.ChangeFailureRating = RateChangeFailureRate(metrics.ChangeFailureRate);
        metrics.IsChangeFailureRateEstimated = cfrUsingFallback;

        // ── MTTR (Time to Restore Service) ───────────────────────────────────────
        // DORA defines this as time from production incident to full service restoration.
        // Use deployment-tagged runs only; fallback to all runs when none are tagged.
        var mttrUsingFallback = !allDeploymentRuns.Any();
        var runsForMttr = mttrUsingFallback ? periodRuns : allDeploymentRuns;
        metrics.MeanTimeToRestoreHours = ComputeMttr(runsForMttr);
        metrics.MttrRating = RateMttr(metrics.MeanTimeToRestoreHours);
        metrics.IsMttrEstimated = mttrUsingFallback;

        // ── Rework Rate (work-item state-transition churn) ────────────────────────
        // Counts work items that transitioned FROM a done state BACK TO an active state,
        // divided by total completions. Returns 0 when no work-item events are available.
        metrics.ReworkRate = WorkItemReworkCalculator.Compute(workItemEvents);
        metrics.ReworkRateRating = RateReworkRate(metrics.ReworkRate);
        metrics.IsReworkRateEstimated = workItemEvents.Count == 0;

        await repo.SaveAsync(metrics, cancellationToken);

        logger.LogInformation(
            "DORA_COMPUTE: Saved metrics for OrgId={OrgId}, ProjectId={ProjectId} — " +
            "DF={DF:F2}/day (estimated={DFEst}), LT={LT:F2}h (approx={LTApprox}), CFR={CFR:F1}% (estimated={CFREst}), MTTR={MTTR:F2}h (estimated={MTTREst}), ReworkRate={RR:F1}% (estimated={RREst})",
            SanitizeForLog(orgId), SanitizeForLog(projectId),
            metrics.DeploymentFrequency, metrics.IsDeploymentFrequencyEstimated,
            metrics.LeadTimeForChangesHours, metrics.IsLeadTimeApproximate,
            metrics.ChangeFailureRate, metrics.IsChangeFailureRateEstimated,
            metrics.MeanTimeToRestoreHours, metrics.IsMttrEstimated,
            metrics.ReworkRate, metrics.IsReworkRateEstimated);

        return metrics;
    }

    // ── Lead Time from PR + deploy linkage ───────────────────────────────────────
    /// <summary>
    /// Real DORA lead time: for each completed (merged) PR, find the first successful
    /// deployment that finished AFTER the PR's close time. Returns the average hours
    /// across at least <see cref="MinPrLinkagesForRealLeadTime"/> linkages, or null
    /// when there is insufficient PR/deploy signal to be meaningful.
    ///
    /// Why this approach:
    ///   • PR ClosedAt is the closest available proxy for "code merged to main"
    ///   • The next successful deployment is the first time those changes ran in production
    ///   • Outliers above 60 days are dropped — stale PRs that merged into long-running
    ///     branches would otherwise dominate the average
    /// </summary>
    private const int MinPrLinkagesForRealLeadTime = 3;

    private static double? ComputeLeadTimeFromPrAndDeploys(
        List<PullRequestEventDto> prEvents,
        List<PipelineRunDto> successfulDeployments)
    {
        if (prEvents.Count == 0) return null;
        if (successfulDeployments.Count == 0) return null;

        var mergedPrs = prEvents
            .Where(p => p.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
                        && p.ClosedAt.HasValue)
            .ToList();

        if (mergedPrs.Count == 0) return null;

        // Sort deploys by their finish (or start) time once, into a parallel array
        // of effective deploy timestamps so binary search can target it directly.
        var orderedDeploys = successfulDeployments
            .Where(d => (d.FinishTime ?? d.StartTime) != default)
            .OrderBy(d => d.FinishTime ?? d.StartTime)
            .ToList();

        if (orderedDeploys.Count == 0) return null;

        var deployTimes = new DateTimeOffset[orderedDeploys.Count];
        for (var i = 0; i < orderedDeploys.Count; i++)
            deployTimes[i] = orderedDeploys[i].FinishTime ?? orderedDeploys[i].StartTime;

        // Walk merged PRs in chronological order so we can advance a single
        // monotonically-increasing pointer into deployTimes — O(PR + Deployments)
        // instead of the previous O(PR * Deployments) LINQ FirstOrDefault scan.
        // For the very first PR (or after a regression in PR order) we fall back
        // to Array.BinarySearch which is O(log Deployments).
        var sortedPrs = mergedPrs
            .OrderBy(p => p.ClosedAt!.Value)
            .ToList();

        var leadHours = new List<double>(sortedPrs.Count);
        var cursor = 0;
        foreach (var pr in sortedPrs)
        {
            var mergedAt = pr.ClosedAt!.Value;

            // Advance the cursor until deployTimes[cursor] >= mergedAt.
            while (cursor < deployTimes.Length && deployTimes[cursor] < mergedAt)
                cursor++;

            if (cursor >= deployTimes.Length) break; // no future deploy for any remaining PR

            var deployTime = deployTimes[cursor];
            var hours = (deployTime - mergedAt).TotalHours;
            if (hours <= 0 || hours > 60 * 24) continue;

            leadHours.Add(hours);
        }

        if (leadHours.Count < MinPrLinkagesForRealLeadTime) return null;
        return leadHours.Average();
    }

    // ── Log-forging prevention ────────────────────────────────────────────────────
    // Strip control chars from caller-supplied strings before they reach log sinks.
    private static string SanitizeForLog(string value) =>
        Velo.Api.Logging.LogSanitizer.SanitiseForLog(value);

    // ── MTTR: average time from a failure until the next success ─────────────────
    // Uses FinishTime so a long-running failed build isn't measured as instant restoration.
    // Only runs that have a FinishTime contribute — partial runs are skipped.
    private static double ComputeMttr(List<PipelineRunDto> runs)
    {
        var sorted = runs
            .Where(r => r.FinishTime.HasValue)
            .OrderBy(r => r.FinishTime!.Value)
            .ToList();

        var restoreTimes = new List<double>();

        for (int i = 0; i < sorted.Count; i++)
        {
            if (!sorted[i].Result.Equals("failed", StringComparison.OrdinalIgnoreCase)) continue;

            var failure = sorted[i];
            var nextSuccess = sorted
                .Skip(i + 1)
                .FirstOrDefault(r =>
                    r.PipelineName == failure.PipelineName &&
                    r.Result.Equals("succeeded", StringComparison.OrdinalIgnoreCase));

            if (nextSuccess == null) continue;

            var restoreHours = (nextSuccess.FinishTime!.Value - failure.FinishTime!.Value).TotalHours;
            if (restoreHours > 0) restoreTimes.Add(restoreHours);
        }

        return restoreTimes.Any() ? restoreTimes.Average() : 0;
    }

    // ── Rating helpers ────────────────────────────────────────────────────────────
    // Based on DORA 2024 benchmarks (https://dora.dev/guides/dora-metrics/)

    private static string RateDeploymentFrequency(double deploymentsPerDay) => deploymentsPerDay switch
    {
        >= 1.0 => "Elite",   // Multiple deploys per day
        >= 1.0 / 7 => "High",   // At least weekly
        >= 1.0 / 30 => "Medium", // At least monthly
        _ => "Low"
    };

    private static string RateLeadTime(double hours) => hours switch
    {
        <= 1 => "Elite",   // < 1 hour
        <= 24 => "High",   // < 1 day
        <= 168 => "Medium", // < 1 week
        _ => "Low"
    };

    private static string RateChangeFailureRate(double percent) => percent switch
    {
        <= 15 => "Elite",
        <= 30 => "High",
        <= 45 => "Medium",
        _ => "Low"
    };

    private static string RateMttr(double hours) => hours switch
    {
        <= 1 => "Elite",
        <= 24 => "High",
        <= 168 => "Medium",
        _ => "Low"
    };

    private static string RateReworkRate(double percent) => percent switch
    {
        <= 4 => "Elite",
        <= 8 => "High",
        <= 32 => "Medium",
        _ => "Low"
    };
}
