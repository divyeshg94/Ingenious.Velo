using Microsoft.EntityFrameworkCore;
using Velo.Shared.Models;
using Velo.SQL;

namespace Velo.Api.Services;

public interface IPrSizeMetricsService
{
    /// <summary>
    /// Calculate average PR size metrics for a project over a specified period.
    /// Returns aggregated statistics on PR size, reviewers, and approval times.
    /// </summary>
    Task<PrSizeMetricsDto?> GetAveragePrSizeAsync(
        string orgId,
        string projectId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    /// <summary>
    /// Get detailed PR size distribution for insights and charts.
    /// </summary>
    Task<PrSizeDistributionDto> GetPrSizeDistributionAsync(
        string orgId,
        string projectId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    /// <summary>
    /// Get top reviewers by participation and approval patterns.
    /// </summary>
    Task<IEnumerable<ReviewerInsightsDto>> GetTopReviewersAsync(
        string orgId,
        string projectId,
        int topCount,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

/// <summary>
/// Calculates Average PR Size and related Pull Request metrics.
/// Part of Phase 2: Team Health Pull Request Insights.
/// </summary>
public class PrSizeMetricsService(
    VeloDbContext dbContext,
    ILogger<PrSizeMetricsService> logger) : IPrSizeMetricsService
{
    public async Task<PrSizeMetricsDto?> GetAveragePrSizeAsync(
        string orgId,
        string projectId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        try
        {
            dbContext.CurrentOrgId = orgId;

            // Query completed PRs in the time range
            var completedPrs = await dbContext.PullRequestEvents
                .Where(p => p.OrgId == orgId &&
                           p.ProjectId == projectId &&
                           p.Status == "completed" &&
                           p.CreatedAt >= from &&
                           p.CreatedAt <= to &&
                           p.ClosedAt.HasValue)
                .ToListAsync(cancellationToken);

            if (completedPrs.Count == 0)
            {
                return new PrSizeMetricsDto(
                    orgId,
                    projectId,
                    from,
                    to,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    DateTimeOffset.UtcNow);
            }

            var prCount = completedPrs.Count;
            var avgFilesChanged = (int)Math.Round(completedPrs.Average(p => p.FilesChanged));
            var avgLinesAdded = (int)Math.Round(completedPrs.Average(p => p.LinesAdded));
            var avgLinesDeleted = (int)Math.Round(completedPrs.Average(p => p.LinesDeleted));
            var avgTotalChanges = avgLinesAdded + avgLinesDeleted;
            var approvalRate = (decimal)completedPrs.Count(p => p.IsApproved) / prCount * 100;
            var avgReviewerCount = Math.Round(completedPrs.Average(p => p.ReviewerCount), 1);

            // Calculate average review cycle time (for approved PRs only)
            var approvedPrsWithCycleTime = completedPrs
                .Where(p => p.CycleDurationMinutes.HasValue && p.CycleDurationMinutes > 0)
                .ToList();

            var avgCycleDuration = approvedPrsWithCycleTime.Any()
                ? (int)Math.Round(approvedPrsWithCycleTime.Average(p => p.CycleDurationMinutes!.Value))
                : 0;

            logger.LogInformation(
                "PR_METRICS: Calculated for OrgId={OrgId}, ProjectId={ProjectId}: " +
                "PRCount={Count}, AvgFiles={AvgFiles}, AvgLinesAdded={AvgAdded}, AvgLinesDeleted={AvgDeleted}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId),
                prCount, avgFilesChanged, avgLinesAdded, avgLinesDeleted);

            return new PrSizeMetricsDto(
                orgId,
                projectId,
                from,
                to,
                prCount,
                avgFilesChanged,
                avgLinesAdded,
                avgLinesDeleted,
                avgTotalChanges,
                avgCycleDuration,
                Math.Round(approvalRate, 2),
                avgReviewerCount,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "PR_METRICS: Error calculating metrics for OrgId={OrgId}, ProjectId={ProjectId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
            return null;
        }
    }

    public async Task<PrSizeDistributionDto> GetPrSizeDistributionAsync(
        string orgId,
        string projectId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        try
        {
            dbContext.CurrentOrgId = orgId;

            var completedPrs = await dbContext.PullRequestEvents
                .Where(p => p.OrgId == orgId &&
                           p.ProjectId == projectId &&
                           p.Status == "completed" &&
                           p.CreatedAt >= from &&
                           p.CreatedAt <= to)
                .Select(p => p.LinesAdded + p.LinesDeleted)
                .ToListAsync(cancellationToken);

            if (completedPrs.Count == 0)
            {
                return new PrSizeDistributionDto();
            }

            return new PrSizeDistributionDto(
                completedPrs.Count(x => x <= 100),
                completedPrs.Count(x => x > 100 && x <= 500),
                completedPrs.Count(x => x > 500 && x <= 1000),
                completedPrs.Count(x => x > 1000));
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "PR_DISTRIBUTION: Error calculating distribution for OrgId={OrgId}, ProjectId={ProjectId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
            return new PrSizeDistributionDto();
        }
    }

    public async Task<IEnumerable<ReviewerInsightsDto>> GetTopReviewersAsync(
        string orgId,
        string projectId,
        int topCount,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        try
        {
            dbContext.CurrentOrgId = orgId;

            var reviewerData = new Dictionary<string, (int reviews, int approvals, int rejections)>();

            var prs = await dbContext.PullRequestEvents
                .Where(p => p.OrgId == orgId &&
                           p.ProjectId == projectId &&
                           p.CreatedAt >= from &&
                           p.CreatedAt <= to &&
                           !string.IsNullOrEmpty(p.ReviewerNames))
                .ToListAsync(cancellationToken);

            foreach (var pr in prs)
            {
                if (string.IsNullOrEmpty(pr.ReviewerNames)) continue;

                try
                {
                    // Parse reviewer names from JSON array
                    var reviewerNames = System.Text.Json.JsonSerializer.Deserialize<string[]>(pr.ReviewerNames);
                    if (reviewerNames == null) continue;

                    foreach (var reviewer in reviewerNames)
                    {
                        if (string.IsNullOrEmpty(reviewer)) continue;

                        if (!reviewerData.ContainsKey(reviewer))
                        {
                            reviewerData[reviewer] = (0, 0, 0);
                        }

                        var (reviews, approvals, rejections) = reviewerData[reviewer];
                        reviewerData[reviewer] = (
                            reviews + 1,
                            approvals + pr.ApprovedCount,
                            rejections + pr.RejectedCount);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "PR_REVIEWERS: Failed to parse reviewer names for PR {PrId}", pr.PrId);
                }
            }

            var result = reviewerData
                .Select(kvp => new ReviewerInsightsDto(
                    kvp.Key,
                    kvp.Value.reviews,
                    kvp.Value.approvals,
                    kvp.Value.rejections))
                .OrderByDescending(r => r.PrReviewCount)
                .Take(topCount)
                .ToList();

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "PR_REVIEWERS: Error fetching top reviewers for OrgId={OrgId}, ProjectId={ProjectId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
            return Enumerable.Empty<ReviewerInsightsDto>();
        }
    }
}

/// <summary>
/// DTO for average PR size metrics.
/// </summary>
public record PrSizeMetricsDto(
    string OrgId,
    string ProjectId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int TotalPrCount,
    int AverageFilesChanged,
    int AverageLinesAdded,
    int AverageLinesDeleted,
    int AverageTotalChanges,
    int AverageReviewCycleDurationMinutes,
    decimal ApprovalRate,
    double AverageReviewerCount,
    DateTimeOffset ComputedAt);

/// <summary>
/// DTO for PR size distribution buckets.
/// </summary>
public record PrSizeDistributionDto(
    int SmallPrs = 0,
    int MediumPrs = 0,
    int LargePrs = 0,
    int ExtraLargePrs = 0);

/// <summary>
/// DTO for reviewer insights and participation metrics.
/// </summary>
public record ReviewerInsightsDto(
    string ReviewerName,
    int PrReviewCount,
    int ApprovalCount,
    int RejectionCount);
