using Velo.Shared.Contracts;
using Velo.Shared.Models;

namespace Velo.Api.Services;

public interface IDoraComputeService
{
    Task<DoraMetricsDto> ComputeAndSaveAsync(string orgId, string projectId, CancellationToken cancellationToken);
}

/// <summary>
/// Computes DORA metrics from stored pipeline runs and saves them.
/// Called after each ingestion so the dashboard always has fresh metrics.
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

        logger.LogInformation(
            "DORA_COMPUTE: {Total} total runs, {Period} in last {Days} days for OrgId={OrgId}, ProjectId={ProjectId}",
            runs.Count, periodRuns.Count, PeriodDays, orgId, projectId);

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

        // Use all successful runs as fallback when no pipelines are tagged as deployments
        var successfulRuns = periodRuns
            .Where(r => r.Result.Equals("succeeded", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var deploymentsForFreq = deployments.Any() ? deployments : successfulRuns;
        metrics.DeploymentFrequency = deploymentsForFreq.Count / (double)PeriodDays;
        metrics.DeploymentFrequencyRating = RateDeploymentFrequency(metrics.DeploymentFrequency);

        // ── Lead Time (average build duration as proxy) ──────────────────────────
        var completedRuns = periodRuns
            .Where(r => r.DurationMs.HasValue && r.Result.Equals("succeeded", StringComparison.OrdinalIgnoreCase))
            .ToList();

        metrics.LeadTimeForChangesHours = completedRuns.Any()
            ? completedRuns.Average(r => r.DurationMs!.Value) / 3_600_000.0
            : 0;
        metrics.LeadTimeRating = RateLeadTime(metrics.LeadTimeForChangesHours);

        // ── Change Failure Rate ───────────────────────────────────────────────────
        var failedRuns = periodRuns
            .Where(r => r.Result.Equals("failed", StringComparison.OrdinalIgnoreCase))
            .Count();

        metrics.ChangeFailureRate = periodRuns.Any()
            ? failedRuns / (double)periodRuns.Count * 100.0
            : 0;
        metrics.ChangeFailureRating = RateChangeFailureRate(metrics.ChangeFailureRate);

        // ── MTTR ─────────────────────────────────────────────────────────────────
        metrics.MeanTimeToRestoreHours = ComputeMttr(periodRuns);
        metrics.MttrRating = RateMttr(metrics.MeanTimeToRestoreHours);

        // ── Rework Rate (re-runs / total) ─────────────────────────────────────────
        var rerunCount = periodRuns.Count - periodRuns.Select(r => r.PipelineName).Distinct().Count();
        metrics.ReworkRate = periodRuns.Any()
            ? Math.Max(0, rerunCount) / (double)periodRuns.Count * 100.0
            : 0;
        metrics.ReworkRateRating = RateReworkRate(metrics.ReworkRate);

        await repo.SaveAsync(metrics, cancellationToken);

        logger.LogInformation(
            "DORA_COMPUTE: Saved metrics for OrgId={OrgId}, ProjectId={ProjectId} — " +
            "DF={DF:F2}/day, LT={LT:F2}h, CFR={CFR:F1}%, MTTR={MTTR:F2}h",
            orgId, projectId, metrics.DeploymentFrequency, metrics.LeadTimeForChangesHours,
            metrics.ChangeFailureRate, metrics.MeanTimeToRestoreHours);

        return metrics;
    }

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
    // Based on DORA 2023 benchmarks

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
        <= 5 => "Elite",
        <= 10 => "High",
        <= 15 => "Medium",
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
        <= 5 => "Elite",
        <= 10 => "High",
        <= 20 => "Medium",
        _ => "Low"
    };
}
