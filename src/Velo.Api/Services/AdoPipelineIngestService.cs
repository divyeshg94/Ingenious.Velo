using System.Net.Http.Headers;
using System.Text.Json;
using Velo.Shared.Models.Ado;
using Velo.Shared.Contracts;
using Velo.Shared.Models;

namespace Velo.Api.Services;

/// <summary>
/// Calls the Azure DevOps Builds REST API and stores pipeline runs in the database.
/// Uses the user's ADO access token (from SDK.getAccessToken()) — no PAT storage required.
/// </summary>
public class AdoPipelineIngestService(
    IMetricsRepository repo,
    IHttpClientFactory httpClientFactory,
    ILogger<AdoPipelineIngestService> logger)
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
        foreach (var build in builds.Value)
        {
            if (build.StartTime == null || build.FinishTime == null) continue;

            // Skip if already stored
            var alreadyExists = await repo.RunExistsAsync(
                orgId, projectId, build.Definition?.Id ?? 0, build.BuildNumber ?? string.Empty, cancellationToken);
            if (alreadyExists) continue;

            var run = new PipelineRunDto
            {
                Id = Guid.NewGuid(),
                OrgId = orgId,
                ProjectId = projectId,
                AdoPipelineId = build.Definition?.Id ?? 0,
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
                IngestedAt = DateTimeOffset.UtcNow
            };

            await repo.SaveRunAsync(run, cancellationToken);
            saved++;
        }

        logger.LogInformation(
            "ADO_INGEST: Saved {Saved}/{Total} runs for OrgId={OrgId}, ProjectId={ProjectId}",
            saved, builds.Value.Length, orgId, projectId);

        return saved;
    }

    // Heuristic: pipelines with "deploy", "release", "prod" in their name are deployments
    private static bool IsDeploymentPipeline(AdoBuild build)
    {
        var name = (build.Definition?.Name ?? string.Empty).ToLowerInvariant();
        return name.Contains("deploy") || name.Contains("release") || name.Contains("prod");
    }
}
