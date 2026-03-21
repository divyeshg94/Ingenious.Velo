using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Velo.Api.Services;

/// <summary>
/// Registers an Azure DevOps service hook subscription for the build.complete event.
/// Once registered, ADO calls POST /api/webhook/ado every time a pipeline run finishes —
/// no polling, no manual sync required.
/// </summary>
public class AdoServiceHookService(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<AdoServiceHookService> logger)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<bool> EnsureHookRegisteredAsync(
        string orgName,
        string projectName,
        string adoAccessToken,
        string webhookBaseUrl,
        CancellationToken cancellationToken)
    {
        using var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adoAccessToken);

        // ── 1. Resolve project GUID ──────────────────────────────────────────────
        var projectId = await GetProjectIdAsync(http, orgName, projectName, cancellationToken);
        if (projectId == null)
        {
            logger.LogWarning("HOOK: Could not resolve GUID for project {Project} in org {Org}", projectName, orgName);
            return false;
        }

        // ── 2. Check whether a subscription for our endpoint already exists ──────
        var listUrl = $"https://dev.azure.com/{orgName}/_apis/hooks/subscriptions?api-version=7.1";
        var listResp = await http.GetAsync(listUrl, cancellationToken);
        if (listResp.IsSuccessStatusCode)
        {
            var listJson = await listResp.Content.ReadAsStringAsync(cancellationToken);
            var webhookUrl = $"{webhookBaseUrl.TrimEnd('/')}/api/webhook/ado";
            if (listJson.Contains(webhookUrl, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("HOOK: Subscription for {Url} already registered — skipping", webhookUrl);
                return true;
            }
        }

        // ── 3. Register new subscription ─────────────────────────────────────────
        var secret = config["Webhook:Secret"] ?? "velo-webhook-secret";
        var webhookEndpoint = $"{webhookBaseUrl.TrimEnd('/')}/api/webhook/ado";

        var payload = new
        {
            consumerId = "webHooks",
            consumerActionId = "httpRequest",
            eventType = "build.complete",
            publisherId = "tfs",
            publisherInputs = new
            {
                projectId,
                definitionId = "",   // empty = all pipelines in the project
                buildStatus = ""     // empty = all results (succeeded, failed, etc.)
            },
            consumerInputs = new
            {
                url = webhookEndpoint,
                httpHeaders = $"X-Velo-Secret:{secret}",
                resourceDetailsToSend = "all",
                messagesToSend = "none",
                detailedMessagesToSend = "none"
            },
            scope = 1
        };

        var body = new StringContent(
            JsonSerializer.Serialize(payload, _json),
            Encoding.UTF8, "application/json");

        var regUrl = $"https://dev.azure.com/{orgName}/_apis/hooks/subscriptions?api-version=7.1";
        var regResp = await http.PostAsync(regUrl, body, cancellationToken);

        if (regResp.IsSuccessStatusCode)
        {
            logger.LogInformation(
                "HOOK: Service hook registered for org={Org}, project={Project}, endpoint={Url}",
                orgName, projectName, webhookEndpoint);
            return true;
        }

        var errorBody = await regResp.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning(
            "HOOK: Could not register service hook ({Status}). " +
            "This usually means the token lacks 'Service Hooks (write)' permission. " +
            "Register manually at: ADO → Project Settings → Service hooks. Body: {Body}",
            regResp.StatusCode, errorBody);
        return false;
    }

    private static async Task<string?> GetProjectIdAsync(
        HttpClient http, string orgName, string projectName, CancellationToken cancellationToken)
    {
        var url = $"https://dev.azure.com/{orgName}/_apis/projects/{Uri.EscapeDataString(projectName)}?api-version=7.1";
        var resp = await http.GetAsync(url, cancellationToken);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }
}
