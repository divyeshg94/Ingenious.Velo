using System.Net.Http.Headers;
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
    ILogger<AdoPipelineIngestService> logger,
    VeloDbContext dbContext) : IAdoPipelineIngestService
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<int> IngestAsync(
        string orgId,
        string projectId,
        string adoAccessToken,
        CancellationToken cancellationToken)
    {
        // orgId is the ADO organisation name (e.g. "mycompany"), set from SDK.getHost().name
        var url = $"https://dev.azure.com/{orgId}/{Uri.EscapeDataString(projectId)}" +
                  "/_apis/build/builds?api-version=7.1&$top=200&queryOrder=finishTimeDescending" +
                  "&statusFilter=completed";

        logger.LogInformation("ADO_INGEST: Fetching builds from {Url}", url);

        using var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adoAccessToken);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ADO_INGEST: HTTP request failed for OrgId={OrgId}, ProjectId={ProjectId}", orgId, projectId);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError(
                "ADO_INGEST: ADO API returned {Status} for OrgId={OrgId}, ProjectId={ProjectId}. Body: {Body}",
                response.StatusCode, orgId, projectId, body);
            throw new InvalidOperationException(
                $"Azure DevOps API returned {(int)response.StatusCode}: {response.ReasonPhrase}. " +
                "Check the access token scope (vso.build required).");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var builds = JsonSerializer.Deserialize<AdoBuildsResponse>(json, _json);

        if (builds?.Value == null || builds.Value.Length == 0)
        {
            logger.LogInformation("ADO_INGEST: No builds found for OrgId={OrgId}, ProjectId={ProjectId}", orgId, projectId);
            return 0;
        }

        logger.LogInformation(
            "ADO_INGEST: Fetched {Count} builds for OrgId={OrgId}, ProjectId={ProjectId}",
            builds.Value.Length, orgId, projectId);

        int saved = 0;
        Dictionary<int, string?> repoCache = new();
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
            if (alreadyExists) continue;

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
                IsDeployment = IsDeploymentPipeline(build),
                TriggeredBy = build.RequestedBy?.DisplayName,
                RepositoryName = repoName,
                IngestedAt = DateTimeOffset.UtcNow
            };

            await repo.SaveRunAsync(run, cancellationToken);
            saved++;
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
                backfilled, orgId, projectId);

        logger.LogInformation(
            "ADO_INGEST: Saved {Saved}/{Total} runs for OrgId={OrgId}, ProjectId={ProjectId}",
            saved, builds.Value.Length, orgId, projectId);

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
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adoAccessToken);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(projectsUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AUTO_SYNC: HTTP request to list projects failed for OrgId={OrgId}", orgId);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "AUTO_SYNC: Project list returned {Status} for OrgId={OrgId}. Body: {Body}",
                response.StatusCode, orgId, body);
            // Non-fatal: return 0 so the caller can still update LastSyncedAt
            return 0;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var projects = JsonSerializer.Deserialize<AdoProjectsResponse>(json, _json);

        if (projects?.Value == null || projects.Value.Length == 0)
        {
            logger.LogInformation("AUTO_SYNC: No projects found for OrgId={OrgId}", orgId);
            return 0;
        }

        logger.LogInformation(
            "AUTO_SYNC: Found {Count} projects for OrgId={OrgId} — starting per-project ingest",
            projects.Value.Length, orgId);

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
                logger.LogWarning(ex, "AUTO_SYNC: Failed to save project mapping for {Project}", project.Name);
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
                    await doraComputeService.ComputeAndSaveAsync(orgId, project.Name, cancellationToken);
            }
            catch (Exception ex)
            {
                // Log and continue — one failing project should not abort the whole sync
                logger.LogWarning(ex,
                    "AUTO_SYNC: Ingest failed for project {Project} (OrgId={OrgId}) — skipping",
                    project.Name, orgId);
            }
        }

        logger.LogInformation(
            "AUTO_SYNC: Completed — {Total} runs ingested across {Projects} projects for OrgId={OrgId}",
            total, projects.Value.Length, orgId);

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

    // Heuristic: pipelines with "deploy", "release", "prod" in their name are deployments
    private static bool IsDeploymentPipeline(AdoBuild build)
    {
        var name = (build.Definition?.Name ?? string.Empty).ToLowerInvariant();
        return name.Contains("deploy") || name.Contains("release") || name.Contains("prod");
    }
}
