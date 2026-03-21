using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Velo.Shared.Models;

namespace Velo.Api.Services;

/// <summary>
/// Manages Azure DevOps service hook subscriptions for the build.complete event.
/// Requires the extension to declare the vso.hooks_write scope.
/// </summary>
public class AdoServiceHookService(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<AdoServiceHookService> logger)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    // ── Status ────────────────────────────────────────────────────────────────────

    public async Task<WebhookStatusDto> GetStatusAsync(
        string orgName, string projectName, string adoToken,
        string webhookBaseUrl, CancellationToken cancellationToken)
    {
        var webhookUrl = $"{webhookBaseUrl.TrimEnd('/')}/api/webhook/ado";

        using var http = CreateClient(adoToken);
        var listUrl = $"https://dev.azure.com/{orgName}/_apis/hooks/subscriptions?api-version=7.1";
        var listResp = await http.GetAsync(listUrl, cancellationToken);

        if (!listResp.IsSuccessStatusCode)
        {
            var err = await listResp.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("HOOK_STATUS: Failed to list subscriptions ({Status}): {Err}", listResp.StatusCode, err);
            return new WebhookStatusDto
            {
                IsRegistered = false,
                RegistrationError = $"Could not query subscriptions ({(int)listResp.StatusCode}): {ExtractMessage(err)}",
                ManualSetupUrl = ManualSetupUrl(orgName)
            };
        }

        using var doc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync(cancellationToken));
        if (!doc.RootElement.TryGetProperty("value", out var subs)) return NotRegistered(orgName, webhookUrl);

        foreach (var sub in subs.EnumerateArray())
        {
            var consumerInputs = sub.TryGetProperty("consumerInputs", out var ci) ? ci : default;
            var url = consumerInputs.ValueKind == JsonValueKind.Object &&
                      consumerInputs.TryGetProperty("url", out var u) ? u.GetString() : null;

            if (!string.Equals(url, webhookUrl, StringComparison.OrdinalIgnoreCase)) continue;

            return new WebhookStatusDto
            {
                IsRegistered = true,
                SubscriptionId = sub.TryGetProperty("id", out var id) ? id.GetString() : null,
                WebhookUrl = webhookUrl,
                ProjectId = projectName,
                CreatedDate = sub.TryGetProperty("createdDate", out var cd) ? cd.GetString() : null,
            };
        }

        return NotRegistered(orgName, webhookUrl);
    }

    // ── Register ──────────────────────────────────────────────────────────────────

    public async Task<WebhookStatusDto> RegisterAsync(
        string orgName, string projectName, string adoToken,
        string webhookBaseUrl, CancellationToken cancellationToken)
    {
        var webhookUrl = $"{webhookBaseUrl.TrimEnd('/')}/api/webhook/ado";
        using var http = CreateClient(adoToken);

        // Resolve project GUID
        var projectId = await GetProjectIdAsync(http, orgName, projectName, cancellationToken);
        if (projectId == null)
        {
            return new WebhookStatusDto
            {
                IsRegistered = false,
                RegistrationError = $"Could not resolve project '{projectName}' in org '{orgName}'. Verify the project name and that the token has vso.build scope.",
                ManualSetupUrl = ManualSetupUrl(orgName)
            };
        }

        var secret = config["Webhook:Secret"] ?? "velo-webhook-secret";
        var payload = new
        {
            publisherId = "tfs",
            eventType = "build.complete",
            resourceVersion = "1.0",
            consumerId = "webHooks",
            consumerActionId = "httpRequest",
            publisherInputs = new
            {
                projectId,       // project GUID — required
                definitionName = "",  // empty = all pipeline definitions
                buildStatus = ""      // empty = all statuses (succeeded, failed, etc.)
            },
            consumerInputs = new
            {
                url = webhookUrl,
                httpHeaders = $"X-Velo-Secret:{secret}",
                resourceDetailsToSend = "All",   // capitalized — ADO API requirement
                messagesToSend = "None",
                detailedMessagesToSend = "None"
            }
        };

        var body = new StringContent(JsonSerializer.Serialize(payload, _json), Encoding.UTF8, "application/json");
        var regUrl = $"https://dev.azure.com/{orgName}/_apis/hooks/subscriptions?api-version=7.1";
        var regResp = await http.PostAsync(regUrl, body, cancellationToken);

        if (regResp.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(await regResp.Content.ReadAsStringAsync(cancellationToken));
            var newId = doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;

            logger.LogInformation("HOOK: Registered subscription {Id} for org={Org}, project={Project}", newId, orgName, projectName);

            return new WebhookStatusDto
            {
                IsRegistered = true,
                SubscriptionId = newId,
                WebhookUrl = webhookUrl,
                ProjectId = projectName
            };
        }

        var errorBody = await regResp.Content.ReadAsStringAsync(cancellationToken);
        var errorMsg = $"ADO returned {(int)regResp.StatusCode}: {ExtractMessage(errorBody)}";

        if ((int)regResp.StatusCode == 401 || (int)regResp.StatusCode == 403)
            errorMsg = $"Permission denied ({(int)regResp.StatusCode}). The extension needs the 'vso.hooks_write' scope. " +
                       "Re-publish the extension and re-authorize it in ADO, then try again.";

        logger.LogWarning("HOOK: Registration failed — {Error}", errorMsg);
        return new WebhookStatusDto
        {
            IsRegistered = false,
            RegistrationError = errorMsg,
            ManualSetupUrl = ManualSetupUrl(orgName)
        };
    }

    // ── Remove ────────────────────────────────────────────────────────────────────

    public async Task<bool> RemoveAsync(
        string orgName, string subscriptionId, string adoToken,
        CancellationToken cancellationToken)
    {
        using var http = CreateClient(adoToken);
        var url = $"https://dev.azure.com/{orgName}/_apis/hooks/subscriptions/{subscriptionId}?api-version=7.1";
        var resp = await http.DeleteAsync(url, cancellationToken);

        if (resp.IsSuccessStatusCode)
        {
            logger.LogInformation("HOOK: Removed subscription {Id} for org={Org}", subscriptionId, orgName);
            return true;
        }

        logger.LogWarning("HOOK: Failed to remove subscription {Id} ({Status})", subscriptionId, resp.StatusCode);
        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private HttpClient CreateClient(string adoToken)
    {
        var http = httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adoToken);
        return http;
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

    private static WebhookStatusDto NotRegistered(string orgName, string webhookUrl) => new()
    {
        IsRegistered = false,
        WebhookUrl = webhookUrl,
        ManualSetupUrl = ManualSetupUrl(orgName)
    };

    private static string ManualSetupUrl(string orgName) =>
        $"https://dev.azure.com/{orgName}/_settings/serviceHooks";

    private static string ExtractMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("message", out var m)) return m.GetString() ?? json;
            if (doc.RootElement.TryGetProperty("errorCode", out var e)) return e.GetString() ?? json;
        }
        catch { }
        return json.Length > 200 ? json[..200] : json;
    }
}
