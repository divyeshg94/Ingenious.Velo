using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Velo.Shared.Models.Ado;
using Velo.Shared.Contracts;
using Velo.Shared.Models;
using Velo.SQL;

namespace Velo.Api.Services;

public interface IAdoPipelineIngestService
{
    Task<int> IngestAsync(string orgId, string projectId, string adoAccessToken, CancellationToken cancellationToken);
    Task<int> IngestAllProjectsAsync(string orgId, string orgUrl, string adoAccessToken, CancellationToken cancellationToken);
}

/// <summary>
/// Calls the Azure DevOps Builds REST API and stores pipeline runs in the database.
/// Uses the user's ADO access token (from SDK.getAccessToken()) — no PAT storage required.
/// </summary>
public class AdoPipelineIngestService(
    IMetricsRepository repo,
    IHttpClientFactory httpClientFactory,
    IDoraComputeService doraComputeService,
    IAdoTimelineService timelineService,
    ILogger<AdoPipelineIngestService> logger,
    VeloDbContext dbContext) : IAdoPipelineIngestService
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private const int PageSize = 200;
    private const int MaxPages = 25;             // 25 × 200 = 5,000 builds — safety cap
    private const int IngestLookbackDays = 60;   // bound first-sync work

    public async Task<int> IngestAsync(
        string orgId,
        string projectId,
        string adoAccessToken,
        CancellationToken cancellationToken)
    {
        var minTime = DateTimeOffset.UtcNow.AddDays(-IngestLookbackDays).ToString("O");

        using var http = httpClientFactory.CreateClient();
        // Explicit timeout prevents thread-pool exhaustion if ADO is slow or unreachable.
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adoAccessToken);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        int saved = 0;
        int totalFetched = 0;
        int page = 0;
        string? continuationToken = null;
        Dictionary<int, string?> repoCache = new();

        // Paginate via the ADO `x-ms-continuationtoken` response header. The previous
        // implementation fetched only the latest 200 builds on first sync, which silently
        // truncated history for any customer with more than 200 builds in the lookback
        // window. We now walk pages until ADO stops returning a continuation token,
        // we hit a page where every build already exists (incremental short-circuit),
        // or the safety cap kicks in.
        while (page < MaxPages)
        {
            page++;

            var urlBuilder = new StringBuilder(
                $"https://dev.azure.com/{orgId}/{Uri.EscapeDataString(projectId)}" +
                $"/_apis/build/builds?api-version=7.1&$top={PageSize}" +
                "&queryOrder=finishTimeDescending&statusFilter=completed" +
                $"&minTime={Uri.EscapeDataString(minTime)}");

            if (!string.IsNullOrEmpty(continuationToken))
                urlBuilder.Append("&continuationToken=").Append(Uri.EscapeDataString(continuationToken));

            var url = urlBuilder.ToString();
            logger.LogInformation("ADO_INGEST: Page {Page} GET {Url}", page,
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(url));

            HttpResponseMessage response;
            try
            {
                response = await http.GetAsync(url, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ADO_INGEST: HTTP request failed for OrgId={OrgId}, ProjectId={ProjectId}, Page={Page}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId), page);
                throw;
            }

            // Dispose the response at the end of every iteration; a long-running
            // ingest could otherwise leak HttpResponseMessage instances and
            // contribute to socket exhaustion.
            using var _responseScope = response;

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "ADO_INGEST: ADO API returned {Status} {Reason} for OrgId={OrgId}, ProjectId={ProjectId}, Page={Page}",
                    (int)response.StatusCode, response.ReasonPhrase,
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId), page);
                logger.LogDebug(
                    "ADO_INGEST: Error body for OrgId={OrgId}, ProjectId={ProjectId}, Page={Page}: {Body}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId), page,
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(await response.Content.ReadAsStringAsync(cancellationToken), 1000));
                throw new InvalidOperationException(
                    $"Azure DevOps API returned {(int)response.StatusCode}: {response.ReasonPhrase}. " +
                    "Check the access token scope (vso.build required).");
            }

            // Extract continuation token from response header BEFORE consuming body.
            continuationToken = response.Headers.TryGetValues("x-ms-continuationtoken", out var ct)
                ? ct.FirstOrDefault()
                : null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var builds = JsonSerializer.Deserialize<AdoBuildsResponse>(json, _json);

            if (builds?.Value == null || builds.Value.Length == 0)
            {
                logger.LogInformation(
                    "ADO_INGEST: Page {Page} returned 0 builds — stopping pagination for OrgId={OrgId}, ProjectId={ProjectId}",
                    page,
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
                break;
            }

            totalFetched += builds.Value.Length;

            int pageSaved = 0;
            int pageExisting = 0;
            foreach (var build in builds.Value)
            {
                if (build.StartTime == null || build.FinishTime == null) continue;

                var defId = build.Definition?.Id ?? 0;

                // Resolve repo name once per definition (cached for the whole sync pass)
                if (!repoCache.TryGetValue(defId, out var repoName))
                {
                    repoName = await ResolveRepositoryNameAsync(orgId, projectId, defId, adoAccessToken, http, cancellationToken);
                    repoCache[defId] = repoName;
                }

                // Skip if already stored
                var alreadyExists = await repo.RunExistsAsync(
                    orgId, projectId, defId, build.BuildNumber ?? string.Empty, cancellationToken);
                if (alreadyExists)
                {
                    pageExisting++;
                    continue;
                }

                // Resolve stage names from the build timeline. Failures are logged
                // and ignored — stage capture is best-effort and must not abort ingest.
                var stageName = await timelineService.ResolveStageNamesAsync(
                    orgId, projectId, build.Id, adoAccessToken, cancellationToken);

                var run = new PipelineRunDto
                {
                    Id = Guid.NewGuid(),
                    OrgId = orgId,
                    ProjectId = projectId,
                    AdoPipelineId = defId,
                    PipelineName = build.Definition?.Name ?? "Unknown",
                    RunNumber = build.BuildNumber ?? string.Empty,
                    Result = build.Result ?? "unknown",
                    StartTime = build.StartTime.Value,
                    FinishTime = build.FinishTime,
                    DurationMs = build.FinishTime.HasValue
                        ? (long)(build.FinishTime.Value - build.StartTime.Value).TotalMilliseconds
                        : null,
                    IsDeployment = IsDeploymentPipeline(build, stageName),
                    StageName = stageName,
                    TriggeredBy = build.RequestedBy?.DisplayName,
                    RepositoryName = repoName,
                    IngestedAt = DateTimeOffset.UtcNow
                };

                await repo.SaveRunAsync(run, cancellationToken);
                pageSaved++;
                saved++;
            }

            logger.LogInformation(
                "ADO_INGEST: Page {Page} — fetched={Fetched}, saved={Saved}, existing={Existing}, continuation={HasCont}",
                page, builds.Value.Length, pageSaved, pageExisting, !string.IsNullOrEmpty(continuationToken));

            // Incremental short-circuit: every build in this page was already stored.
            // Older pages are guaranteed to be older still (queryOrder=finishTimeDescending),
            // so they cannot contain anything new. Stop walking.
            if (pageSaved == 0 && pageExisting == builds.Value.Length)
                break;

            if (string.IsNullOrEmpty(continuationToken))
                break;
        }

        // Backfill RepositoryName on pre-existing runs that were ingested before the column existed.
        // Resolve each distinct pipeline definition that still has NULL and bulk-update in one pass.
        var nullRepoPipelineIds = await repo.GetPipelineIdsWithNullRepositoryAsync(orgId, projectId, cancellationToken);
        int backfilled = 0;
        foreach (var defId in nullRepoPipelineIds)
        {
            if (!repoCache.TryGetValue(defId, out var repoName))
            {
                repoName = await ResolveRepositoryNameAsync(orgId, projectId, defId, adoAccessToken, http, cancellationToken);
                repoCache[defId] = repoName;
            }
            if (string.IsNullOrEmpty(repoName)) continue;
            await repo.BackfillRepositoryNameAsync(orgId, projectId, defId, repoName, cancellationToken);
            backfilled++;
        }
        if (backfilled > 0)
            logger.LogInformation(
                "ADO_INGEST: Backfilled RepositoryName for {Count} pipeline definition(s). OrgId={OrgId}, ProjectId={ProjectId}",
                backfilled,
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));

        logger.LogInformation(
            "ADO_INGEST: Saved {Saved} new runs across {Fetched} fetched (pages={Pages}) for OrgId={OrgId}, ProjectId={ProjectId}",
            saved, totalFetched, page,
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));

        return saved;
    }

    /// <summary>
    /// Discovers all projects in the ADO organisation and ingests the latest 200 pipeline runs
    /// for each. Returns the total number of new runs saved across all projects.
    /// Used for first-time backfill when an org connects or when metrics are missing.
    /// </summary>
    public async Task<int> IngestAllProjectsAsync(
        string orgId,
        string orgUrl,
        string adoAccessToken,
        CancellationToken cancellationToken)
    {
        // Derive the short organisation name from the URL
        // e.g. "https://dev.azure.com/mycompany" → "mycompany"
        var orgName = orgId;
        if (Uri.TryCreate(orgUrl.TrimEnd('/'), UriKind.Absolute, out var uri))
            orgName = uri.Segments.LastOrDefault()?.Trim('/') ?? orgId;

        var projectsUrl = $"https://dev.azure.com/{orgName}/_apis/projects?api-version=7.1&$top=200";
        logger.LogInformation("AUTO_SYNC: Fetching project list from {Url}", projectsUrl);

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
            logger.LogError(ex, "AUTO_SYNC: HTTP request to list projects failed for OrgId={OrgId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "AUTO_SYNC: Project list returned {Status} {Reason} for OrgId={OrgId}",
                (int)response.StatusCode, response.ReasonPhrase,
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));
            logger.LogDebug(
                "AUTO_SYNC: Error body for OrgId={OrgId}: {Body}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(await response.Content.ReadAsStringAsync(cancellationToken), 1000));
            // Non-fatal: return 0 so the caller can still update LastSyncedAt
            return 0;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var projects = JsonSerializer.Deserialize<AdoProjectsResponse>(json, _json);

        if (projects?.Value == null || projects.Value.Length == 0)
        {
            logger.LogInformation("AUTO_SYNC: No projects found for OrgId={OrgId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));
            return 0;
        }

        logger.LogInformation(
            "AUTO_SYNC: Found {Count} projects for OrgId={OrgId} — starting per-project ingest",
            projects.Value.Length, Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));

        // Persist GUID→name mappings so webhooks can resolve project names without an ADO token
        foreach (var project in projects.Value)
        {
            try
            {
                var existing = await dbContext.ProjectMappings
                    .FirstOrDefaultAsync(m => m.OrgId == orgId && m.ProjectGuid == project.Id, CancellationToken.None);
                if (existing is null)
                {
                    dbContext.ProjectMappings.Add(new Velo.SQL.Models.ProjectMapping
                    {
                        OrgId = orgId,
                        ProjectGuid = project.Id,
                        ProjectName = project.Name,
                        UpdatedAt = DateTimeOffset.UtcNow
                    });
                }
                else
                {
                    existing.ProjectName = project.Name;
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AUTO_SYNC: Failed to save project mapping for {Project}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(project.Name));
            }
        }
        try { await dbContext.SaveChangesAsync(CancellationToken.None); }
        catch (Exception ex) { logger.LogWarning(ex, "AUTO_SYNC: Failed to persist project mappings"); }

        int total = 0;
        foreach (var project in projects.Value)
        {
            try
            {
                var saved = await IngestAsync(orgId, project.Name, adoAccessToken, cancellationToken);
                total += saved;

                // Compute DORA metrics for this project after ingest, even if no new runs
                // were saved — the compute service re-evaluates all stored runs.
                if (saved > 0)
                    await doraComputeService.ComputeAndSaveAsync(orgId, project.Name, repositoryName: null, teamName: null, cancellationToken);
            }
            catch (Exception ex)
            {
                // Log and continue — one failing project should not abort the whole sync
                logger.LogWarning(ex,
                    "AUTO_SYNC: Ingest failed for project {Project} (OrgId={OrgId}) — skipping",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(project.Name),
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));
            }
        }

        logger.LogInformation(
            "AUTO_SYNC: Completed — {Total} runs ingested across {Projects} projects for OrgId={OrgId}",
            total, projects.Value.Length, Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));

        return total;
    }

    private async Task<string?> ResolveRepositoryNameAsync(
        string orgId, string projectId, int definitionId, string adoAccessToken,
        HttpClient http, CancellationToken cancellationToken)
    {
        if (definitionId == 0) return null;

        var url = $"https://dev.azure.com/{orgId}/{Uri.EscapeDataString(projectId)}" +
                  $"/_apis/build/definitions/{definitionId}?api-version=7.1&$select=repository";

        try
        {
            var response = await http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var definition = JsonSerializer.Deserialize<AdoBuildDefinition>(json, _json);
            return definition?.Repository?.Name;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ADO_INGEST: Could not resolve repository for definition {Id}", definitionId);
            return null;
        }
    }

    // Heuristic delegated to DeploymentDetector so all ingestion paths agree.
    private static bool IsDeploymentPipeline(AdoBuild build, string? stageName = null)
        => DeploymentDetector.IsDeployment(build.Definition?.Name, stageName);
}
