using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Velo.Shared.Models.Ado;
using Velo.Shared.Contracts;
using Velo.Shared.Models;
using Velo.SQL;

namespace Velo.Api.Services;

public interface IAdoPrDiffIngestService
{
    /// <summary>
    /// Fetch PR diff metrics (lines added/deleted, files changed, reviewers) from Azure DevOps
    /// and store them in the database. Enriches existing PullRequestEvent records with detailed metrics.
    /// </summary>
    Task<int> IngestPrDiffsAsync(string orgId, string projectId, string adoAccessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Ingest PR diffs for all projects in an organization.
    /// Used during initial backfill or sync operations.
    /// </summary>
    Task<int> IngestPrDiffsAllProjectsAsync(string orgId, string orgUrl, string adoAccessToken, CancellationToken cancellationToken);
}

/// <summary>
/// Fetches PR diff metrics from Azure DevOps Git REST API and enriches PullRequestEvent records.
/// Phase 2: Enables calculation of Average PR Size and reviewer insights.
/// </summary>
public class AdoPrDiffIngestService(
    IMetricsRepository repo,
    IHttpClientFactory httpClientFactory,
    ILogger<AdoPrDiffIngestService> logger,
    VeloDbContext dbContext) : IAdoPrDiffIngestService
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<int> IngestPrDiffsAsync(
        string orgId,
        string projectId,
        string adoAccessToken,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "PR_DIFF_INGEST: Starting for OrgId={OrgId}, ProjectId={ProjectId}",
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));

        using var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adoAccessToken);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Get all repositories in the project
        var reposUrl = $"https://dev.azure.com/{orgId}/{Uri.EscapeDataString(projectId)}/_apis/git/repositories?api-version=7.1";
        
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(reposUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "PR_DIFF_INGEST: Failed to fetch repositories for OrgId={OrgId}, ProjectId={ProjectId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
            return 0;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "PR_DIFF_INGEST: Repository list returned {Status} for OrgId={OrgId}, ProjectId={ProjectId}",
                (int)response.StatusCode,
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
            return 0;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var reposResponse = JsonSerializer.Deserialize<dynamic>(json, _json);
        var repos = ((System.Text.Json.JsonElement)reposResponse!).GetProperty("value");

        int totalEnriched = 0;

        foreach (var repo in repos.EnumerateArray())
        {
            var repoId = repo.GetProperty("id").GetString();
            if (string.IsNullOrEmpty(repoId)) continue;

            try
            {
                var enriched = await IngestPrsFromRepositoryAsync(
                    orgId, projectId, repoId, adoAccessToken, http, cancellationToken);
                totalEnriched += enriched;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "PR_DIFF_INGEST: Failed to ingest diffs for repo {RepoId}",
                    repoId);
                // Continue with next repo
            }
        }

        logger.LogInformation(
            "PR_DIFF_INGEST: Completed — {Total} PRs enriched for OrgId={OrgId}, ProjectId={ProjectId}",
            totalEnriched,
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));

        return totalEnriched;
    }

    public async Task<int> IngestPrDiffsAllProjectsAsync(
        string orgId,
        string orgUrl,
        string adoAccessToken,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "PR_DIFF_AUTO_SYNC: Starting for OrgId={OrgId}",
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));

        var orgName = orgId;
        if (Uri.TryCreate(orgUrl.TrimEnd('/'), UriKind.Absolute, out var uri))
            orgName = uri.Segments.LastOrDefault()?.Trim('/') ?? orgId;

        var projectsUrl = $"https://dev.azure.com/{orgName}/_apis/projects?api-version=7.1&$top=200";

        using var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adoAccessToken);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(projectsUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "PR_DIFF_AUTO_SYNC: Failed to fetch projects for OrgId={OrgId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));
            return 0;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "PR_DIFF_AUTO_SYNC: Project list returned {Status} for OrgId={OrgId}",
                (int)response.StatusCode,
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));
            return 0;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var projects = JsonSerializer.Deserialize<dynamic>(json, _json);
        var projectList = ((System.Text.Json.JsonElement)projects!).GetProperty("value");

        int totalEnriched = 0;

        foreach (var project in projectList.EnumerateArray())
        {
            var projectName = project.GetProperty("name").GetString();
            if (string.IsNullOrEmpty(projectName)) continue;

            try
            {
                var enriched = await IngestPrDiffsAsync(orgId, projectName, adoAccessToken, cancellationToken);
                totalEnriched += enriched;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "PR_DIFF_AUTO_SYNC: Failed for project {Project}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectName));
            }
        }

        logger.LogInformation(
            "PR_DIFF_AUTO_SYNC: Completed — {Total} PRs enriched across all projects for OrgId={OrgId}",
            totalEnriched,
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));

        return totalEnriched;
    }

    private async Task<int> IngestPrsFromRepositoryAsync(
        string orgId,
        string projectId,
        string repoId,
        string adoAccessToken,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        // Fetch all completed PRs from this repository
        var prsUrl = $"https://dev.azure.com/{orgId}/{Uri.EscapeDataString(projectId)}" +
                     $"/_apis/git/repositories/{repoId}/pullrequests?api-version=7.1" +
                     $"&searchCriteria.status=completed&$top=200";

        var response = await http.GetAsync(prsUrl, cancellationToken);
        if (!response.IsSuccessStatusCode) return 0;

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var prsResponse = JsonSerializer.Deserialize<dynamic>(json, _json);
        var prs = ((System.Text.Json.JsonElement)prsResponse!).GetProperty("value");

        int enriched = 0;

        foreach (var prElement in prs.EnumerateArray())
        {
            var prId = prElement.GetProperty("pullRequestId").GetInt32();

            try
            {
                // Fetch diff stats for this PR
                var prDiffs = await FetchPrDiffStatsAsync(
                    orgId, projectId, repoId, prId, adoAccessToken, http, cancellationToken);

                if (prDiffs != null)
                {
                    // Update the PR event with diff metrics
                    var updated = await UpdatePrEventWithDiffsAsync(
                        orgId, projectId, prId, prDiffs, cancellationToken);
                    if (updated) enriched++;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex,
                    "PR_DIFF_INGEST: Failed to fetch diffs for PR {PrId}",
                    prId);
            }
        }

        return enriched;
    }

    private async Task<(int FilesChanged, int LinesAdded, int LinesDeleted)?> FetchPrDiffStatsAsync(
        string orgId,
        string projectId,
        string repoId,
        int prId,
        string adoAccessToken,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        // Fetch PR iterations to get diff statistics
        var iterationsUrl = $"https://dev.azure.com/{orgId}/{Uri.EscapeDataString(projectId)}" +
                           $"/_apis/git/repositories/{repoId}/pullrequests/{prId}/iterations" +
                           $"?api-version=7.1";

        try
        {
            var response = await http.GetAsync(iterationsUrl, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var iterations = JsonSerializer.Deserialize<AdoPullRequestIterationsResponse>(json, _json);

            if (iterations?.Value == null || iterations.Value.Length == 0)
                return null;

            // Get the last iteration (most recent diff)
            var lastIteration = iterations.Value[^1];
            var changes = lastIteration.IterationChanges;

            if (changes == null) return null;

            var filesChanged = (changes.ChangeCountAdd ?? 0) + (changes.ChangeCountEdit ?? 0) +
                             (changes.ChangeCountDelete ?? 0) + (changes.ChangeCountRename ?? 0);
            var linesAdded = changes.ChangeCountAdd ?? 0;
            var linesDeleted = changes.ChangeCountDelete ?? 0;

            return (filesChanged, linesAdded, linesDeleted);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "PR_DIFF_INGEST: Failed to fetch iterations for PR {PrId}", prId);
            return null;
        }
    }

    private async Task<bool> UpdatePrEventWithDiffsAsync(
        string orgId,
        string projectId,
        int prId,
        (int FilesChanged, int LinesAdded, int LinesDeleted)? diffs,
        CancellationToken cancellationToken)
    {
        if (diffs == null) return false;

        try
        {
            // Set current org context for query filter
            dbContext.CurrentOrgId = orgId;

            // Find the most recent PR event for this PR in this project
            var prEvent = await dbContext.PullRequestEvents
                .Where(p => p.OrgId == orgId && p.ProjectId == projectId && p.PrId == prId)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (prEvent == null) return false;

            // Update with diff metrics
            var diffValue = diffs.Value;
            prEvent.FilesChanged = diffValue.FilesChanged;
            prEvent.LinesAdded = diffValue.LinesAdded;
            prEvent.LinesDeleted = diffValue.LinesDeleted;

            // Calculate cycle duration if approved
            if (prEvent.IsApproved && prEvent.FirstApprovedAt.HasValue)
            {
                var cycleDuration = (int)(prEvent.FirstApprovedAt.Value - prEvent.CreatedAt).TotalMinutes;
                prEvent.CycleDurationMinutes = cycleDuration > 0 ? cycleDuration : 0;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "PR_DIFF_INGEST: Failed to update PR event for PrId={PrId}, ProjectId={ProjectId}",
                prId, Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
            return false;
        }
    }
}
