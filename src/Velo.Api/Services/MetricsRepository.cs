using Microsoft.EntityFrameworkCore;
using Velo.Shared.Models;
using Velo.Shared.Contracts;
using Velo.SQL.Models;
using Velo.SQL;

namespace Velo.Api.Services;

/// <summary>
/// Metrics repository - handles all data access for DORA metrics, pipeline runs, team health, and org context.
/// SECURITY: All queries are automatically scoped to the current org_id via EF Core global query filters.
/// MULTI-TENANCY: Each org only sees their own data - enforced at both application and database layers.
/// </summary>
public class MetricsRepository(VeloDbContext dbContext, ILogger<MetricsRepository> logger) : IMetricsRepository
{
    public async Task<DoraMetricsDto?> GetLatestAsync(string orgId, string projectId, CancellationToken cancellationToken)
    {
        try
        {
            var metric = await dbContext.DoraMetrics
                .AsNoTracking()
                .Where(m => m.OrgId == orgId && m.ProjectId == projectId)
                .OrderByDescending(m => m.ComputedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (metric == null)
            {
                logger.LogInformation("No latest metrics found for OrgId: {OrgId}, ProjectId: {ProjectId}", orgId, projectId);
                return null;
            }

            return MapDoraMetricsToDto(metric);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching latest DORA metrics for OrgId: {OrgId}, ProjectId: {ProjectId}", orgId, projectId);
            throw;
        }
    }

    public async Task<IEnumerable<DoraMetricsDto>> GetHistoryAsync(string orgId, string projectId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        try
        {
            var metrics = await dbContext.DoraMetrics
                .AsNoTracking()
                .Where(m => m.OrgId == orgId && m.ProjectId == projectId && m.ComputedAt >= from && m.ComputedAt <= to)
                .OrderByDescending(m => m.ComputedAt)
                .ToListAsync(cancellationToken);

            logger.LogInformation(
                "Retrieved {MetricCount} historical DORA records for OrgId: {OrgId}, ProjectId: {ProjectId}, Range: {From} to {To}",
                metrics.Count, orgId, projectId, from, to);

            return metrics.Select(MapDoraMetricsToDto).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching DORA metrics history for OrgId: {OrgId}, ProjectId: {ProjectId}", orgId, projectId);
            throw;
        }
    }

    public async Task SaveAsync(DoraMetricsDto metricsDto, CancellationToken cancellationToken)
    {
        try
        {
            var metric = new DoraMetrics
            {
                Id = metricsDto.Id == Guid.Empty ? Guid.NewGuid() : metricsDto.Id,
                OrgId = metricsDto.OrgId,
                ProjectId = metricsDto.ProjectId,
                ComputedAt = metricsDto.ComputedAt,
                PeriodStart = metricsDto.PeriodStart,
                PeriodEnd = metricsDto.PeriodEnd,
                DeploymentFrequency = metricsDto.DeploymentFrequency,
                DeploymentFrequencyRating = metricsDto.DeploymentFrequencyRating,
                LeadTimeForChangesHours = metricsDto.LeadTimeForChangesHours,
                LeadTimeRating = metricsDto.LeadTimeRating,
                ChangeFailureRate = metricsDto.ChangeFailureRate,
                ChangeFailureRating = metricsDto.ChangeFailureRating,
                MeanTimeToRestoreHours = metricsDto.MeanTimeToRestoreHours,
                MttrRating = metricsDto.MttrRating,
                ReworkRate = metricsDto.ReworkRate,
                ReworkRateRating = metricsDto.ReworkRateRating
            };

            dbContext.DoraMetrics.Add(metric);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Saved DORA metrics for OrgId: {OrgId}, ProjectId: {ProjectId}, MetricId: {MetricId}",
                metricsDto.OrgId, metricsDto.ProjectId, metric.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving DORA metrics for OrgId: {OrgId}, ProjectId: {ProjectId}", metricsDto.OrgId, metricsDto.ProjectId);
            throw;
        }
    }

    public async Task<IEnumerable<PipelineRunDto>> GetRunsAsync(string orgId, string projectId, int page, int pageSize, CancellationToken cancellationToken)
    {
        try
        {
            var skip = (page - 1) * pageSize;
            var runs = await dbContext.PipelineRuns
                .AsNoTracking()
                .Where(r => r.OrgId == orgId && r.ProjectId == projectId)
                .OrderByDescending(r => r.StartTime)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            logger.LogInformation(
                "Retrieved {RunCount} pipeline runs for OrgId: {OrgId}, ProjectId: {ProjectId}, Page: {Page}, PageSize: {PageSize}",
                runs.Count, orgId, projectId, page, pageSize);

            return runs.Select(MapPipelineRunToDto).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching pipeline runs for OrgId: {OrgId}, ProjectId: {ProjectId}", orgId, projectId);
            throw;
        }
    }

    public async Task<bool> RunExistsAsync(string orgId, string projectId, int adoPipelineId, string runNumber, CancellationToken cancellationToken)
    {
        return await dbContext.PipelineRuns
            .AsNoTracking()
            .AnyAsync(r => r.OrgId == orgId && r.ProjectId == projectId
                        && r.AdoPipelineId == adoPipelineId && r.RunNumber == runNumber,
                cancellationToken);
    }

    public async Task SaveRunAsync(PipelineRunDto runDto, CancellationToken cancellationToken)
    {
        try
        {
            var run = new PipelineRun
            {
                Id = runDto.Id == Guid.Empty ? Guid.NewGuid() : runDto.Id,
                OrgId = runDto.OrgId,
                ProjectId = runDto.ProjectId,
                AdoPipelineId = runDto.AdoPipelineId,
                PipelineName = runDto.PipelineName,
                RunNumber = runDto.RunNumber,
                Result = runDto.Result,
                StartTime = runDto.StartTime,
                FinishTime = runDto.FinishTime,
                DurationMs = runDto.DurationMs,
                IsDeployment = runDto.IsDeployment,
                StageName = runDto.StageName,
                TriggeredBy = runDto.TriggeredBy,
                IngestedAt = runDto.IngestedAt
            };

            dbContext.PipelineRuns.Add(run);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Saved pipeline run for OrgId: {OrgId}, ProjectId: {ProjectId}, PipelineId: {PipelineId}, RunId: {RunId}",
                runDto.OrgId, runDto.ProjectId, runDto.AdoPipelineId, run.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving pipeline run for OrgId: {OrgId}, ProjectId: {ProjectId}", runDto.OrgId, runDto.ProjectId);
            throw;
        }
    }

    public async Task<TeamHealthDto?> GetTeamHealthAsync(string orgId, string projectId, CancellationToken cancellationToken)
    {
        try
        {
            var health = await dbContext.TeamHealth
                .AsNoTracking()
                .Where(h => h.OrgId == orgId && h.ProjectId == projectId)
                .OrderByDescending(h => h.ComputedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (health == null)
            {
                logger.LogInformation("No team health data found for OrgId: {OrgId}, ProjectId: {ProjectId}", orgId, projectId);
                return null;
            }

            return MapTeamHealthToDto(health);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching team health for OrgId: {OrgId}, ProjectId: {ProjectId}", orgId, projectId);
            throw;
        }
    }

    public async Task SaveTeamHealthAsync(TeamHealthDto healthDto, CancellationToken cancellationToken)
    {
        try
        {
            var health = new TeamHealth
            {
                Id = healthDto.Id == Guid.Empty ? Guid.NewGuid() : healthDto.Id,
                OrgId = healthDto.OrgId,
                ProjectId = healthDto.ProjectId,
                ComputedAt = healthDto.ComputedAt,
                CodingTimeHours = healthDto.CodingTimeHours,
                ReviewTimeHours = healthDto.ReviewTimeHours,
                MergeTimeHours = healthDto.MergeTimeHours,
                DeployTimeHours = healthDto.DeployTimeHours,
                AveragePrSizeLines = healthDto.AveragePrSizeLines,
                PrCommentDensity = healthDto.PrCommentDensity,
                PrApprovalRate = healthDto.PrApprovalRate,
                TestPassRate = healthDto.TestPassRate,
                FlakyTestRate = healthDto.FlakyTestRate,
                DeploymentRiskScore = healthDto.DeploymentRiskScore
            };

            dbContext.TeamHealth.Add(health);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Saved team health data for OrgId: {OrgId}, ProjectId: {ProjectId}, HealthId: {HealthId}",
                healthDto.OrgId, healthDto.ProjectId, health.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving team health for OrgId: {OrgId}, ProjectId: {ProjectId}", healthDto.OrgId, healthDto.ProjectId);
            throw;
        }
    }

    public async Task<OrgContextDto?> GetOrgContextAsync(string orgId, CancellationToken cancellationToken)
    {
        try
        {
            var org = await dbContext.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrgId == orgId, cancellationToken);

            if (org == null)
            {
                logger.LogInformation("Organization not found for OrgId: {OrgId}", orgId);
                return null;
            }

            return MapOrgContextToDto(org);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching organization context for OrgId: {OrgId}", orgId);
            throw;
        }
    }

    public async Task SaveOrgContextAsync(OrgContextDto orgDto, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await dbContext.Organizations.FirstOrDefaultAsync(o => o.OrgId == orgDto.OrgId, cancellationToken);

            if (existing != null)
            {
                existing.OrgUrl = orgDto.OrgUrl;
                existing.DisplayName = orgDto.DisplayName;
                existing.IsPremium = orgDto.IsPremium;
                existing.DailyTokenBudget = orgDto.DailyTokenBudget;
                existing.LastSeenAt = DateTimeOffset.UtcNow;
                dbContext.Organizations.Update(existing);
            }
            else
            {
                var org = new OrgContext
                {
                    OrgId = orgDto.OrgId,
                    OrgUrl = orgDto.OrgUrl,
                    DisplayName = orgDto.DisplayName,
                    IsPremium = orgDto.IsPremium,
                    DailyTokenBudget = orgDto.DailyTokenBudget,
                    RegisteredAt = DateTimeOffset.UtcNow,
                    LastSeenAt = DateTimeOffset.UtcNow
                };
                dbContext.Organizations.Add(org);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Saved organization context for OrgId: {OrgId}, URL: {OrgUrl}, Premium: {IsPremium}",
                orgDto.OrgId, orgDto.OrgUrl, orgDto.IsPremium);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving organization context for OrgId: {OrgId}", orgDto.OrgId);
            throw;
        }
    }

    // Private mapping methods
    private static DoraMetricsDto MapDoraMetricsToDto(DoraMetrics metric) => new()
    {
        Id = metric.Id,
        OrgId = metric.OrgId,
        ProjectId = metric.ProjectId,
        ComputedAt = metric.ComputedAt,
        PeriodStart = metric.PeriodStart,
        PeriodEnd = metric.PeriodEnd,
        DeploymentFrequency = metric.DeploymentFrequency,
        DeploymentFrequencyRating = metric.DeploymentFrequencyRating,
        LeadTimeForChangesHours = metric.LeadTimeForChangesHours,
        LeadTimeRating = metric.LeadTimeRating,
        ChangeFailureRate = metric.ChangeFailureRate,
        ChangeFailureRating = metric.ChangeFailureRating,
        MeanTimeToRestoreHours = metric.MeanTimeToRestoreHours,
        MttrRating = metric.MttrRating,
        ReworkRate = metric.ReworkRate,
        ReworkRateRating = metric.ReworkRateRating
    };

    private static PipelineRunDto MapPipelineRunToDto(PipelineRun run) => new()
    {
        Id = run.Id,
        OrgId = run.OrgId,
        ProjectId = run.ProjectId,
        AdoPipelineId = run.AdoPipelineId,
        PipelineName = run.PipelineName,
        RunNumber = run.RunNumber,
        Result = run.Result,
        StartTime = run.StartTime,
        FinishTime = run.FinishTime,
        DurationMs = run.DurationMs,
        IsDeployment = run.IsDeployment,
        StageName = run.StageName,
        TriggeredBy = run.TriggeredBy,
        IngestedAt = run.IngestedAt
    };

    private static TeamHealthDto MapTeamHealthToDto(TeamHealth health) => new()
    {
        Id = health.Id,
        OrgId = health.OrgId,
        ProjectId = health.ProjectId,
        ComputedAt = health.ComputedAt,
        CodingTimeHours = health.CodingTimeHours,
        ReviewTimeHours = health.ReviewTimeHours,
        MergeTimeHours = health.MergeTimeHours,
        DeployTimeHours = health.DeployTimeHours,
        AveragePrSizeLines = health.AveragePrSizeLines,
        PrCommentDensity = health.PrCommentDensity,
        PrApprovalRate = health.PrApprovalRate,
        TestPassRate = health.TestPassRate,
        FlakyTestRate = health.FlakyTestRate,
        DeploymentRiskScore = health.DeploymentRiskScore
    };

    private static OrgContextDto MapOrgContextToDto(OrgContext org) => new()
    {
        OrgId = org.OrgId,
        OrgUrl = org.OrgUrl,
        DisplayName = org.DisplayName,
        IsPremium = org.IsPremium,
        DailyTokenBudget = org.DailyTokenBudget,
        RegisteredAt = org.RegisteredAt,
        LastSeenAt = org.LastSeenAt
    };
}
