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
                logger.LogInformation("No latest metrics found for OrgId: {OrgId}, ProjectId: {ProjectId}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
                return null;
            }

            return MapDoraMetricsToDto(metric);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching latest DORA metrics for OrgId: {OrgId}, ProjectId: {ProjectId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
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
                metrics.Count,
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId),
                from, to);

            return metrics.Select(MapDoraMetricsToDto).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching DORA metrics history for OrgId: {OrgId}, ProjectId: {ProjectId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
            throw;
        }
    }

    public async Task SaveAsync(DoraMetricsDto metricsDto, CancellationToken cancellationToken)
    {
        // Upsert by (OrgId, ProjectId, ComputedDate) so the webhook can recompute
        // many times per day without writing a new row per build. Uniqueness is
        // enforced by the UX_DoraMetrics_OrgId_ProjectId_ComputedDate index — see
        // migration 20260602193035_DoraMetricsComputedDate — which means concurrent
        // recomputes can race on insert, but the second one will hit a unique-
        // constraint violation that we catch and convert to an update. This is the
        // atomic "MERGE" pattern recommended by the SQL Server team for low-rate
        // contention.
        var bucketDate = metricsDto.ComputedAt.UtcDateTime.Date;

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var existing = await dbContext.DoraMetrics
                    .FirstOrDefaultAsync(
                        m => m.OrgId == metricsDto.OrgId
                             && m.ProjectId == metricsDto.ProjectId
                             && m.ComputedDate == bucketDate,
                        cancellationToken);

                if (existing is not null)
                {
                    ApplyMetricsToEntity(metricsDto, existing, bucketDate);
                    existing.ModifiedBy = "dora-compute";
                    existing.ModifiedDate = DateTimeOffset.UtcNow;
                    metricsDto.Id = existing.Id;
                }
                else
                {
                    var metric = new DoraMetrics
                    {
                        Id = metricsDto.Id == Guid.Empty ? Guid.NewGuid() : metricsDto.Id,
                        OrgId = metricsDto.OrgId,
                        ProjectId = metricsDto.ProjectId,
                    };
                    ApplyMetricsToEntity(metricsDto, metric, bucketDate);
                    dbContext.DoraMetrics.Add(metric);
                    metricsDto.Id = metric.Id;
                }

                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogInformation(
                    "Saved DORA metrics for OrgId: {OrgId}, ProjectId: {ProjectId}, MetricId: {MetricId}, Upsert: {Upsert}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(metricsDto.OrgId),
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(metricsDto.ProjectId),
                    metricsDto.Id, existing is not null);
                return;
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex) && attempt < maxAttempts)
            {
                // A concurrent SaveAsync for the same (OrgId, ProjectId, ComputedDate)
                // beat us to the INSERT. Detach our pending entity, re-read, and try
                // again as an UPDATE.
                foreach (var entry in dbContext.ChangeTracker.Entries<DoraMetrics>().ToList())
                    entry.State = EntityState.Detached;

                logger.LogInformation(
                    "DORA metrics upsert race detected for OrgId={OrgId}, ProjectId={ProjectId}, attempt={Attempt} — retrying",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(metricsDto.OrgId),
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(metricsDto.ProjectId),
                    attempt);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error saving DORA metrics for OrgId: {OrgId}, ProjectId: {ProjectId}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(metricsDto.OrgId),
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(metricsDto.ProjectId));
                throw;
            }
        }

        throw new InvalidOperationException(
            $"Failed to upsert DORA metrics for {metricsDto.OrgId}/{metricsDto.ProjectId} after {maxAttempts} attempts.");
    }

    private static void ApplyMetricsToEntity(DoraMetricsDto dto, DoraMetrics entity, DateTime bucketDate)
    {
        entity.ComputedAt = dto.ComputedAt;
        entity.ComputedDate = bucketDate;
        entity.PeriodStart = dto.PeriodStart;
        entity.PeriodEnd = dto.PeriodEnd;
        entity.DeploymentFrequency = dto.DeploymentFrequency;
        entity.DeploymentFrequencyRating = dto.DeploymentFrequencyRating;
        entity.IsDeploymentFrequencyEstimated = dto.IsDeploymentFrequencyEstimated;
        entity.LeadTimeForChangesHours = dto.LeadTimeForChangesHours;
        entity.LeadTimeRating = dto.LeadTimeRating;
        entity.IsLeadTimeApproximate = dto.IsLeadTimeApproximate;
        entity.ChangeFailureRate = dto.ChangeFailureRate;
        entity.ChangeFailureRating = dto.ChangeFailureRating;
        entity.IsChangeFailureRateEstimated = dto.IsChangeFailureRateEstimated;
        entity.MeanTimeToRestoreHours = dto.MeanTimeToRestoreHours;
        entity.MttrRating = dto.MttrRating;
        entity.IsMttrEstimated = dto.IsMttrEstimated;
        entity.ReworkRate = dto.ReworkRate;
        entity.ReworkRateRating = dto.ReworkRateRating;
        entity.IsReworkRateEstimated = dto.IsReworkRateEstimated;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // SQL Server reports 2627 (PRIMARY KEY violation) or 2601 (UNIQUE INDEX violation).
        return ex.InnerException is Microsoft.Data.SqlClient.SqlException sql
               && (sql.Number == 2627 || sql.Number == 2601);
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
                runs.Count,
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId),
                page, pageSize);

            return runs.Select(MapPipelineRunToDto).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching pipeline runs for OrgId: {OrgId}, ProjectId: {ProjectId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
            throw;
        }
    }

    public async Task<IEnumerable<PipelineRunDto>> GetRunsInPeriodAsync(
        string orgId, string projectId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        try
        {
            var runs = await dbContext.PipelineRuns
                .AsNoTracking()
                .Where(r => r.OrgId == orgId && r.ProjectId == projectId
                            && r.StartTime >= from && r.StartTime < to)
                .OrderByDescending(r => r.StartTime)
                .ToListAsync(cancellationToken);

            logger.LogInformation(
                "Retrieved {RunCount} pipeline runs in period for OrgId: {OrgId}, ProjectId: {ProjectId}, From: {From}, To: {To}",
                runs.Count,
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId),
                from, to);

            return runs.Select(MapPipelineRunToDto).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching pipeline runs in period for OrgId: {OrgId}, ProjectId: {ProjectId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
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
                IngestedAt = runDto.IngestedAt,
                RepositoryName = runDto.RepositoryName
            };

            dbContext.PipelineRuns.Add(run);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Saved pipeline run for OrgId: {OrgId}, ProjectId: {ProjectId}, PipelineId: {PipelineId}, RunId: {RunId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(runDto.OrgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(runDto.ProjectId),
                runDto.AdoPipelineId, run.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving pipeline run for OrgId: {OrgId}, ProjectId: {ProjectId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(runDto.OrgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(runDto.ProjectId));
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
                logger.LogInformation("No team health data found for OrgId: {OrgId}, ProjectId: {ProjectId}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
                return null;
            }

            return MapTeamHealthToDto(health);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching team health for OrgId: {OrgId}, ProjectId: {ProjectId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
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
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(healthDto.OrgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(healthDto.ProjectId),
                health.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving team health for OrgId: {OrgId}, ProjectId: {ProjectId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(healthDto.OrgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(healthDto.ProjectId));
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
                logger.LogInformation("Organization not found for OrgId: {OrgId}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));
                return null;
            }

            return MapOrgContextToDto(org);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching organization context for OrgId: {OrgId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));
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
                // Only update LastSyncedAt when the caller explicitly sets it
                if (orgDto.LastSyncedAt.HasValue)
                    existing.LastSyncedAt = orgDto.LastSyncedAt;
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
                    LastSeenAt = DateTimeOffset.UtcNow,
                    LastSyncedAt = orgDto.LastSyncedAt
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

    public async Task SavePrEventAsync(PullRequestEventDto prDto, CancellationToken cancellationToken)
    {
        try
        {
            // Upsert: if a PR event for the same prId+status already exists, update it (e.g. vote changed).
            var existing = await dbContext.PullRequestEvents
                .FirstOrDefaultAsync(p => p.OrgId == prDto.OrgId
                                       && p.ProjectId == prDto.ProjectId
                                       && p.PrId == prDto.PrId
                                       && p.Status == prDto.Status,
                    cancellationToken);

            if (existing != null)
            {
                existing.ClosedAt            = prDto.ClosedAt;
                existing.IsApproved          = prDto.IsApproved;
                existing.ReviewerCount       = prDto.ReviewerCount;
                existing.Title               = prDto.Title;
                existing.FilesChanged        = prDto.FilesChanged;
                existing.LinesAdded          = prDto.LinesAdded;
                existing.LinesDeleted        = prDto.LinesDeleted;
                existing.ReviewerNames       = prDto.ReviewerNames;
                existing.ApprovedCount       = prDto.ApprovedCount;
                existing.RejectedCount       = prDto.RejectedCount;
                existing.FirstApprovedAt     = prDto.FirstApprovedAt;
                existing.CycleDurationMinutes= prDto.CycleDurationMinutes;
                existing.IngestedAt          = DateTimeOffset.UtcNow;
                dbContext.PullRequestEvents.Update(existing);
            }
            else
            {
                var entity = new PullRequestEvent
                {
                    Id                    = prDto.Id == Guid.Empty ? Guid.NewGuid() : prDto.Id,
                    OrgId                 = prDto.OrgId,
                    ProjectId             = prDto.ProjectId,
                    PrId                  = prDto.PrId,
                    Title                 = prDto.Title,
                    Status                = prDto.Status,
                    SourceBranch          = prDto.SourceBranch,
                    TargetBranch          = prDto.TargetBranch,
                    CreatedAt             = prDto.CreatedAt,
                    ClosedAt              = prDto.ClosedAt,
                    IsApproved            = prDto.IsApproved,
                    ReviewerCount         = prDto.ReviewerCount,
                    FilesChanged          = prDto.FilesChanged,
                    LinesAdded            = prDto.LinesAdded,
                    LinesDeleted          = prDto.LinesDeleted,
                    ReviewerNames         = prDto.ReviewerNames,
                    ApprovedCount         = prDto.ApprovedCount,
                    RejectedCount         = prDto.RejectedCount,
                    FirstApprovedAt       = prDto.FirstApprovedAt,
                    CycleDurationMinutes  = prDto.CycleDurationMinutes,
                    IngestedAt            = prDto.IngestedAt
                };
                dbContext.PullRequestEvents.Add(entity);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Saved PR event OrgId={OrgId}, ProjectId={ProjectId}, PrId={PrId}, Status={Status}",
                prDto.OrgId, prDto.ProjectId, prDto.PrId, prDto.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error saving PR event OrgId={OrgId}, ProjectId={ProjectId}, PrId={PrId}",
                prDto.OrgId, prDto.ProjectId, prDto.PrId);
            throw;
        }
    }

    public async Task<bool> PrEventExistsAsync(
        string orgId, string projectId, int prId, string status, CancellationToken cancellationToken)
    {
        return await dbContext.PullRequestEvents
            .AsNoTracking()
            .AnyAsync(p => p.OrgId == orgId && p.ProjectId == projectId
                        && p.PrId == prId && p.Status == status,
                cancellationToken);
    }

    public async Task<IEnumerable<PullRequestEventDto>> GetPrEventsAsync(
        string orgId, string projectId, DateTimeOffset from, CancellationToken cancellationToken)
    {
        var events = await dbContext.PullRequestEvents
            .AsNoTracking()
            .Where(p => p.OrgId == orgId && p.ProjectId == projectId && p.CreatedAt >= from)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return events.Select(p => new PullRequestEventDto
        {
            Id                   = p.Id,
            OrgId                = p.OrgId,
            ProjectId            = p.ProjectId,
            PrId                 = p.PrId,
            Title                = p.Title,
            Status               = p.Status,
            SourceBranch         = p.SourceBranch,
            TargetBranch         = p.TargetBranch,
            CreatedAt            = p.CreatedAt,
            ClosedAt             = p.ClosedAt,
            IsApproved           = p.IsApproved,
            ReviewerCount        = p.ReviewerCount,
            // Phase 2 diff metrics — required by TeamHealth, PR Insights, and DORA
            // Lead Time computation. The earlier projection dropped these and the
            // dashboard silently rendered zeros.
            FilesChanged         = p.FilesChanged,
            LinesAdded           = p.LinesAdded,
            LinesDeleted         = p.LinesDeleted,
            ReviewerNames        = p.ReviewerNames,
            ApprovedCount        = p.ApprovedCount,
            RejectedCount        = p.RejectedCount,
            FirstApprovedAt      = p.FirstApprovedAt,
            CycleDurationMinutes = p.CycleDurationMinutes,
            IngestedAt           = p.IngestedAt,
        }).ToList();
    }

    // ── Work Item Events ───────────────────────────────────────────────────────────

    public async Task SaveWorkItemEventAsync(WorkItemEventDto dto, CancellationToken cancellationToken)
    {
        try
        {
            dbContext.WorkItemEvents.Add(new WorkItemEvent
            {
                Id           = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id,
                OrgId        = dto.OrgId,
                ProjectId    = dto.ProjectId,
                WorkItemId   = dto.WorkItemId,
                WorkItemType = dto.WorkItemType,
                OldState     = dto.OldState,
                NewState     = dto.NewState,
                ChangedAt    = dto.ChangedAt,
                IngestedAt   = dto.IngestedAt,
                CreatedBy    = "webhook",
                ModifiedBy   = "webhook",
            });
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Saved WorkItemEvent OrgId={OrgId}, WI={WorkItemId}, {OldState}→{NewState}",
                dto.OrgId, dto.WorkItemId, dto.OldState, dto.NewState);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error saving WorkItemEvent OrgId={OrgId}, WI={WorkItemId}",
                dto.OrgId, dto.WorkItemId);
            throw;
        }
    }

    public async Task<IEnumerable<WorkItemEventDto>> GetWorkItemEventsAsync(
        string orgId, string projectId, DateTimeOffset from, CancellationToken cancellationToken)
    {
        var events = await dbContext.WorkItemEvents
            .AsNoTracking()
            .Where(w => w.OrgId == orgId && w.ProjectId == projectId && w.ChangedAt >= from)
            .OrderByDescending(w => w.ChangedAt)
            .ToListAsync(cancellationToken);

        return events.Select(w => new WorkItemEventDto
        {
            Id           = w.Id,
            OrgId        = w.OrgId,
            ProjectId    = w.ProjectId,
            WorkItemId   = w.WorkItemId,
            WorkItemType = w.WorkItemType,
            OldState     = w.OldState,
            NewState     = w.NewState,
            ChangedAt    = w.ChangedAt,
            IngestedAt   = w.IngestedAt,
        }).ToList();
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
        IsDeploymentFrequencyEstimated = metric.IsDeploymentFrequencyEstimated,
        LeadTimeForChangesHours = metric.LeadTimeForChangesHours,
        LeadTimeRating = metric.LeadTimeRating,
        IsLeadTimeApproximate = metric.IsLeadTimeApproximate,
        ChangeFailureRate = metric.ChangeFailureRate,
        ChangeFailureRating = metric.ChangeFailureRating,
        IsChangeFailureRateEstimated = metric.IsChangeFailureRateEstimated,
        MeanTimeToRestoreHours = metric.MeanTimeToRestoreHours,
        MttrRating = metric.MttrRating,
        IsMttrEstimated = metric.IsMttrEstimated,
        ReworkRate = metric.ReworkRate,
        ReworkRateRating = metric.ReworkRateRating,
        IsReworkRateEstimated = metric.IsReworkRateEstimated
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
        IngestedAt = run.IngestedAt,
        RepositoryName = run.RepositoryName
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
        LastSeenAt = org.LastSeenAt,
        LastSyncedAt = org.LastSyncedAt
    };

    // ── Repository Discovery ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns all distinct pipeline definition IDs whose runs have a NULL RepositoryName
    /// for the given org/project — used during sync to backfill the column.
    /// </summary>
    public async Task<IEnumerable<int>> GetPipelineIdsWithNullRepositoryAsync(
        string orgId, string projectId, CancellationToken cancellationToken)
    {
        return await dbContext.PipelineRuns
            .AsNoTracking()
            .Where(r => r.OrgId == orgId && r.ProjectId == projectId
                     && (r.RepositoryName == null || r.RepositoryName == string.Empty))
            .Select(r => r.AdoPipelineId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Sets RepositoryName on all runs for a given pipeline definition that currently have NULL.
    /// </summary>
    public async Task BackfillRepositoryNameAsync(
        string orgId, string projectId, int adoPipelineId, string repositoryName,
        CancellationToken cancellationToken)
    {
        var runs = await dbContext.PipelineRuns
            .Where(r => r.OrgId == orgId && r.ProjectId == projectId
                     && r.AdoPipelineId == adoPipelineId
                     && (r.RepositoryName == null || r.RepositoryName == string.Empty))
            .ToListAsync(cancellationToken);

        if (runs.Count == 0) return;
        foreach (var r in runs) r.RepositoryName = repositoryName;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<string>> GetDistinctRepositoriesAsync(
        string orgId, string projectId, CancellationToken cancellationToken)
    {
        return await dbContext.PipelineRuns
            .AsNoTracking()
            .Where(r => r.OrgId == orgId && r.ProjectId == projectId
                     && r.RepositoryName != null && r.RepositoryName != string.Empty)
            .Select(r => r.RepositoryName!)
            .Distinct()
            .OrderBy(r => r)
            .ToListAsync(cancellationToken);
    }

    // ── Team Mappings ──────────────────────────────────────────────────────────────

    public async Task<IEnumerable<TeamMappingDto>> GetTeamMappingsAsync(
        string orgId, string projectId, CancellationToken cancellationToken)
    {
        var mappings = await dbContext.TeamMappings
            .AsNoTracking()
            .Where(m => m.OrgId == orgId && m.ProjectId == projectId && !m.IsDeleted)
            .OrderBy(m => m.TeamName).ThenBy(m => m.RepositoryName)
            .ToListAsync(cancellationToken);

        return mappings.Select(m => new TeamMappingDto
        {
            Id = m.Id,
            OrgId = m.OrgId,
            ProjectId = m.ProjectId,
            RepositoryName = m.RepositoryName,
            TeamName = m.TeamName
        });
    }

    public async Task<TeamMappingDto?> GetTeamMappingAsync(
        string orgId, string projectId, string repositoryName, CancellationToken cancellationToken)
    {
        var m = await dbContext.TeamMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.OrgId == orgId && m.ProjectId == projectId
                && m.RepositoryName == repositoryName && !m.IsDeleted, cancellationToken);

        return m is null ? null : new TeamMappingDto
        {
            Id = m.Id,
            OrgId = m.OrgId,
            ProjectId = m.ProjectId,
            RepositoryName = m.RepositoryName,
            TeamName = m.TeamName
        };
    }

    public async Task SaveTeamMappingAsync(TeamMappingDto dto, CancellationToken cancellationToken)
    {
        // Upsert: find existing active mapping for this repo
        var existing = await dbContext.TeamMappings
            .FirstOrDefaultAsync(m => m.OrgId == dto.OrgId && m.ProjectId == dto.ProjectId
                && m.RepositoryName == dto.RepositoryName && !m.IsDeleted, cancellationToken);

        if (existing is not null)
        {
            existing.TeamName = dto.TeamName;
            existing.ModifiedBy = "api";
            existing.ModifiedDate = DateTimeOffset.UtcNow;
        }
        else
        {
            dbContext.TeamMappings.Add(new Velo.SQL.Models.TeamMapping
            {
                Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id,
                OrgId = dto.OrgId,
                ProjectId = dto.ProjectId,
                RepositoryName = dto.RepositoryName,
                TeamName = dto.TeamName,
                CreatedBy = "api",
                ModifiedBy = "api",
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteTeamMappingAsync(Guid id, string orgId, CancellationToken cancellationToken)
    {
        var mapping = await dbContext.TeamMappings
            .FirstOrDefaultAsync(m => m.Id == id && m.OrgId == orgId, cancellationToken);

        if (mapping is not null)
        {
            mapping.IsDeleted = true;
            mapping.ModifiedBy = "api";
            mapping.ModifiedDate = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
