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

        var runs = (await repo.GetRunsAsync(orgId, projectId, 1, 500, cancellationToken)).ToList();
        var periodRuns = runs.Where(r => r.StartTime >= from).ToList();

        var workItemEvents = (await repo.GetWorkItemEventsAsync(orgId, projectId, from, cancellationToken))
            .ToList();

        logger.LogInformation(
            "DORA_COMPUTE: {Total} total runs, {Period} in last {Days} days, {WiCount} WI events — OrgId={OrgId}, ProjectId={ProjectId}",
            runs.Count, periodRuns.Count, PeriodDays, workItemEvents.Count,
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

        // ── Lead Time (average build duration — proxy) ───────────────────────────
        // True lead time (PR merge → production deploy) requires PR-event linkage,
        // which is not yet implemented. We use average successful-run duration as a proxy.
        var completedRuns = periodRuns
            .Where(r => r.DurationMs.HasValue && r.Result.Equals("succeeded", StringComparison.OrdinalIgnoreCase))
            .ToList();

        metrics.LeadTimeForChangesHours = completedRuns.Any()
            ? completedRuns.Average(r => r.DurationMs!.Value) / 3_600_000.0
            : 0;
        metrics.LeadTimeRating = RateLeadTime(metrics.LeadTimeForChangesHours);
        metrics.IsLeadTimeApproximate = true; // always a proxy until PR linkage is available

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
            "DF={DF:F2}/day (estimated={DFEst}), LT={LT:F2}h (approx), CFR={CFR:F1}% (estimated={CFREst}), MTTR={MTTR:F2}h (estimated={MTTREst}), ReworkRate={RR:F1}% (estimated={RREst})",
            SanitizeForLog(orgId), SanitizeForLog(projectId),
            metrics.DeploymentFrequency, metrics.IsDeploymentFrequencyEstimated,
            metrics.LeadTimeForChangesHours,
            metrics.ChangeFailureRate, metrics.IsChangeFailureRateEstimated,
            metrics.MeanTimeToRestoreHours, metrics.IsMttrEstimated,
            metrics.ReworkRate, metrics.IsReworkRateEstimated);

        return metrics;
    }

    // ── Log-forging prevention ────────────────────────────────────────────────────
    // Strip control chars from caller-supplied strings before they reach log sinks.
    private static string SanitizeForLog(string value) =>
        Velo.Api.Logging.LogSanitizer.SanitiseForLog(value);

    // ── MTTR: average time from a failure until the next success ─────────────────
    private static double ComputeMttr(List<PipelineRunDto> runs)
    {
        var sorted = runs.OrderBy(r => r.StartTime).ToList();
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

            var restoreHours = (nextSuccess.StartTime - failure.StartTime).TotalHours;
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
