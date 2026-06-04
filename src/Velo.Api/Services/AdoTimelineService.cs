using Velo.Api.Logging;

namespace Velo.Api.Services;

/// <summary>
/// Fetches the build timeline from Azure DevOps and produces a concatenated
/// stage-name string suitable for <c>PipelineRun.StageName</c>.
///
/// The ADO <c>build.complete</c> service-hook payload does not carry stage
/// names directly, so we resolve them after the fact via the Timeline REST
/// endpoint:
///   GET /{org}/{project}/_apis/build/builds/{buildId}/timeline?api-version=7.1
///
/// Returns null on any failure (missing token, HTTP error, malformed JSON).
/// Callers MUST tolerate null and continue — never let stage capture failures
/// take down the webhook or ingest pipeline.
/// </summary>
public interface IAdoTimelineService
{
    Task<string?> ResolveStageNamesAsync(
        string organizationName,
        string projectName,
        int buildId,
        string? accessToken,
        CancellationToken cancellationToken);
}

public sealed class AdoTimelineService(
    IHttpClientFactory httpClientFactory,
    ILogger<AdoTimelineService> logger) : IAdoTimelineService
{
    public async Task<string?> ResolveStageNamesAsync(
        string organizationName,
        string projectName,
        int buildId,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogDebug(
                "Skipping timeline fetch: no access token available for OrgName={OrgName} BuildId={BuildId}",
                LogSanitizer.SanitiseForLog(organizationName),
                buildId);
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient("ado");
            var url = $"https://dev.azure.com/{Uri.EscapeDataString(organizationName)}/{Uri.EscapeDataString(projectName)}/_apis/build/builds/{buildId}/timeline?api-version=7.1";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = BuildAuthHeader(accessToken);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Timeline fetch returned non-success status: OrgName={OrgName} BuildId={BuildId} Status={Status}",
                    LogSanitizer.SanitiseForLog(organizationName),
                    buildId,
                    (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return AdoTimelineParser.ExtractStageNames(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Timeline fetch failed: OrgName={OrgName} BuildId={BuildId}",
                LogSanitizer.SanitiseForLog(organizationName),
                buildId);
            return null;
        }
    }

    private static System.Net.Http.Headers.AuthenticationHeaderValue BuildAuthHeader(string token)
    {
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token["Bearer ".Length..]);
        }

        if (token.Contains('.'))
        {
            return new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        var basic = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{token}"));
        return new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
    }
}
