using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Velo.Functions.Models;
using Velo.SQL;
using Velo.SQL.Models;

namespace Velo.Functions.Services;

public interface IMetricsEngine
{
    Task ComputeAllOrgsAsync(CancellationToken cancellationToken);
    Task ProcessPipelineRunAsync(PipelineRunEvent pipelineRun, CancellationToken cancellationToken);
}

public class MetricsEngine(VeloDbContext db, ILogger<MetricsEngine> logger) : IMetricsEngine
{
    private const int PeriodDays = 30;

    /// <summary>
    /// Iterates all active orgs and recomputes all five DORA metrics via pure in-process aggregation.
    /// No AI cost. CurrentOrgId = null bypasses EF Core query filters so we can read all orgs.
    /// </summary>
    public async Task ComputeAllOrgsAsync(CancellationToken cancellationToken)
    {
        // CurrentOrgId is null — EF Core global filter (CurrentOrgId == null || ...) passes for all rows
        var orgProjects = await db.PipelineRuns
            .AsNoTracking()
            .Where(r => !r.IsDeleted)
            .Select(r => new { r.OrgId, r.ProjectId })
            .Distinct()
            .ToListAsync(cancellationToken);

        logger.LogInformation(
            "METRICS_ENGINE: Computing DORA metrics for {Count} org+project combinations",
            orgProjects.Count);

        foreach (var op in orgProjects)
        {
            try
            {
                await ComputeAndSaveAsync(op.OrgId, op.ProjectId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "METRICS_ENGINE: Failed for OrgId={OrgId}, ProjectId={ProjectId}", op.OrgId, op.ProjectId);
            }
        }
    }

    /// <summary>
    /// Persists a single pipeline run event then triggers a targeted recompute for that project.
    /// </summary>
    public async Task ProcessPipelineRunAsync(PipelineRunEvent pipelineRun, CancellationToken cancellationToken)
    {
        var exists = await db.PipelineRuns
            .AsNoTracking()
            .AnyAsync(r => r.OrgId == pipelineRun.OrgId
                        && r.AdoPipelineId == pipelineRun.PipelineId
                        && r.RunNumber == pipelineRun.RunNumber, cancellationToken);

        if (!exists)
        {
            var finish = pipelineRun.FinishTime;
            db.PipelineRuns.Add(new PipelineRun
            {
                OrgId = pipelineRun.OrgId,
                ProjectId = pipelineRun.ProjectId,
                AdoPipelineId = pipelineRun.PipelineId,
                PipelineName = pipelineRun.PipelineName,
                RunNumber = pipelineRun.RunNumber,
                Result = pipelineRun.Result,
                StartTime = pipelineRun.StartTime,
                FinishTime = finish,
                DurationMs = (long)(finish - pipelineRun.StartTime).TotalMilliseconds,
                IsDeployment = pipelineRun.IsDeployment,
                StageName = pipelineRun.StageName,
                TriggeredBy = pipelineRun.TriggeredBy,
                CreatedBy = "functions",
                ModifiedBy = "functions",
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        await ComputeAndSaveAsync(pipelineRun.OrgId, pipelineRun.ProjectId, cancellationToken);
    }

    private async Task ComputeAndSaveAsync(string orgId, string projectId, CancellationToken cancellationToken)
    {
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-PeriodDays);

        var periodRuns = await db.PipelineRuns
            .AsNoTracking()
            .Where(r => r.OrgId == orgId && r.ProjectId == projectId
                     && r.StartTime >= from && !r.IsDeleted)
            .ToListAsync(cancellationToken);

        logger.LogInformation(
            "METRICS_ENGINE: {Count} runs in {Days}-day period — OrgId={OrgId}, ProjectId={ProjectId}",
            periodRuns.Count, PeriodDays, orgId, projectId);

        // ── Deployment Frequency ──────────────────────────────────────────────────
        var deployments = periodRuns
            .Where(r => r.IsDeployment && r.Result.Equals("succeeded", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var successfulRuns = periodRuns
            .Where(r => r.Result.Equals("succeeded", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var forFreq = deployments.Any() ? deployments : successfulRuns;
        var deployFreq = forFreq.Count / (double)PeriodDays;

        // ── Lead Time (build duration as proxy for change-to-deploy) ─────────────
        var completedRuns = periodRuns
            .Where(r => r.DurationMs.HasValue
                     && r.Result.Equals("succeeded", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var leadTimeHours = completedRuns.Any()
            ? completedRuns.Average(r => r.DurationMs!.Value) / 3_600_000.0
            : 0;

        // ── Change Failure Rate ──────────────────────────────────────────────────
        var failedCount = periodRuns.Count(r => r.Result.Equals("failed", StringComparison.OrdinalIgnoreCase));
        var cfr = periodRuns.Any() ? failedCount / (double)periodRuns.Count * 100.0 : 0;

        // ── MTTR ─────────────────────────────────────────────────────────────────
        var mttr = ComputeMttr(periodRuns);

        // ── Rework Rate ──────────────────────────────────────────────────────────
        var uniquePipelines = periodRuns.Select(r => r.PipelineName).Distinct().Count();
        var reruns = Math.Max(0, periodRuns.Count - uniquePipelines);
        var reworkRate = periodRuns.Any() ? reruns / (double)periodRuns.Count * 100.0 : 0;

        db.DoraMetrics.Add(new DoraMetrics
        {
            OrgId = orgId,
            ProjectId = projectId,
            ComputedAt = to,
            PeriodStart = from,
            PeriodEnd = to,
            DeploymentFrequency = deployFreq,
            DeploymentFrequencyRating = RateDeployFreq(deployFreq),
            LeadTimeForChangesHours = leadTimeHours,
            LeadTimeRating = RateLeadTime(leadTimeHours),
            ChangeFailureRate = cfr,
            ChangeFailureRating = RateCfr(cfr),
            MeanTimeToRestoreHours = mttr,
            MttrRating = RateMttr(mttr),
            ReworkRate = reworkRate,
            ReworkRateRating = RateReworkRate(reworkRate),
            CreatedBy = "functions-timer",
            ModifiedBy = "functions-timer",
        });
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "METRICS_ENGINE: Saved — OrgId={OrgId}, ProjectId={ProjectId}, DeployFreq={Freq:F3}/day, CFR={CFR:F1}%",
            orgId, projectId, deployFreq, cfr);
    }

    private static double ComputeMttr(List<PipelineRun> runs)
    {
        var values = new List<double>();
        foreach (var group in runs.GroupBy(r => r.PipelineName))
        {
            PipelineRun? lastFailure = null;
            foreach (var run in group.OrderBy(r => r.StartTime))
            {
                if (run.Result.Equals("failed", StringComparison.OrdinalIgnoreCase))
                {
                    lastFailure = run;
                }
                else if (lastFailure is not null && run.Result.Equals("succeeded", StringComparison.OrdinalIgnoreCase))
                {
                    values.Add((run.StartTime - lastFailure.StartTime).TotalHours);
                    lastFailure = null;
                }
            }
        }
        return values.Any() ? values.Average() : 0;
    }

    private static string RateDeployFreq(double freq) => freq switch
    {
        >= 1 => "Elite",
        >= (1.0 / 7) => "High",
        >= (1.0 / 30) => "Medium",
        _ => "Low",
    };

    private static string RateLeadTime(double hours) => hours switch
    {
        <= 1 => "Elite",
        <= 24 => "High",
        <= 168 => "Medium",
        _ => "Low",
    };

    private static string RateCfr(double rate) => rate switch
    {
        <= 5 => "Elite",
        <= 10 => "High",
        <= 15 => "Medium",
        _ => "Low",
    };

    private static string RateMttr(double hours) => hours switch
    {
        <= 1 => "Elite",
        <= 24 => "High",
        <= 168 => "Medium",
        _ => "Low",
    };

    private static string RateReworkRate(double rate) => rate switch
    {
        <= 5 => "Elite",
        <= 10 => "High",
        <= 15 => "Medium",
        _ => "Low",
    };
}
