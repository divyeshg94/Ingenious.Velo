using Azure.AI.Agents.Persistent;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Velo.Agent;
using Velo.Shared.Models;
using Velo.SQL;
using Velo.SQL.Models;

namespace Velo.Api.Interface;

public interface IAgentConfigService
{
    Task<AgentConfigurationDto?> GetConfigAsync(string orgId, CancellationToken ct = default);
    Task<AgentConfigurationDto> SaveConfigAsync(string orgId, AgentConfigurationDto dto, CancellationToken ct = default);
    Task DeleteConfigAsync(string orgId, CancellationToken ct = default);

    /// <summary>
    /// Tests connectivity to the Foundry endpoint. agentId is optional — when null/empty
    /// the test only verifies the endpoint and credentials are reachable (skips agent lookup).
    /// Supports both API key and service principal auth; falls back to DefaultAzureCredential.
    /// </summary>
    Task<(bool Ok, string Message)> TestConnectionAsync(
        string endpoint, string? agentId,
        string? apiKey,
        string? tenantId, string? clientId, string? clientSecret,
        CancellationToken ct = default);

    /// <summary>
    /// Returns decrypted credentials for internal agent use. Never exposed to the client.
    /// </summary>
    Task<(string? ApiKey, string? TenantId, string? ClientId, string? ClientSecret)>
        GetDecryptedCredentialsAsync(string orgId, CancellationToken ct = default);
}

public class AgentConfigService(VeloDbContext db, IDataProtectionProvider dataProtection) : IAgentConfigService
{
    // Purpose-limited protector — key material is isolated to this use case
    private readonly IDataProtector _protector =
        dataProtection.CreateProtector("Velo.AgentConfig.Credentials.v1");

    public async Task<AgentConfigurationDto?> GetConfigAsync(string orgId, CancellationToken ct = default)
    {
        var cfg = await db.AgentConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.OrgId == orgId, ct);

        return cfg is null ? null : ToDto(cfg);
    }

    public async Task<AgentConfigurationDto> SaveConfigAsync(string orgId, AgentConfigurationDto dto, CancellationToken ct = default)
    {
        var existing = await db.AgentConfigurations
            .FirstOrDefaultAsync(c => c.OrgId == orgId, ct);

        if (existing is null)
        {
            existing = new AgentConfiguration { OrgId = orgId, CreatedAt = DateTimeOffset.UtcNow };
            db.AgentConfigurations.Add(existing);
        }

        existing.FoundryEndpoint = dto.FoundryEndpoint.Trim();
        // Only overwrite AgentId if the client provides one explicitly.
        // Null/empty means "let Velo auto-create it" — preserve any previously auto-created value.
        if (!string.IsNullOrWhiteSpace(dto.AgentId))
            existing.AgentId = dto.AgentId.Trim();
        existing.DisplayName = dto.DisplayName?.Trim();
        existing.DeploymentName = string.IsNullOrWhiteSpace(dto.DeploymentName) ? "gpt-4o" : dto.DeploymentName.Trim();
        existing.IsEnabled = dto.IsEnabled;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        // ── Credential fields: only overwrite when the client sends a new non-empty value ──

        // Option 1: API key
        if (!string.IsNullOrWhiteSpace(dto.ApiKey))
            existing.ApiKey = _protector.Protect(dto.ApiKey.Trim());

        // Option 2: Service principal — all three must be present to update
        if (!string.IsNullOrWhiteSpace(dto.TenantId))
            existing.TenantId = dto.TenantId.Trim();
        if (!string.IsNullOrWhiteSpace(dto.ClientId))
            existing.ClientId = dto.ClientId.Trim();
        if (!string.IsNullOrWhiteSpace(dto.ClientSecret))
            existing.ClientSecret = _protector.Protect(dto.ClientSecret.Trim());

        await db.SaveChangesAsync(ct);
        return ToDto(existing);
    }

    public async Task DeleteConfigAsync(string orgId, CancellationToken ct = default)
    {
        var existing = await db.AgentConfigurations
            .FirstOrDefaultAsync(c => c.OrgId == orgId, ct);

        if (existing is not null)
        {
            db.AgentConfigurations.Remove(existing);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<(string? ApiKey, string? TenantId, string? ClientId, string? ClientSecret)>
        GetDecryptedCredentialsAsync(string orgId, CancellationToken ct = default)
    {
        var cfg = await db.AgentConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.OrgId == orgId, ct);

        if (cfg is null) return (null, null, null, null);

        var apiKey = cfg.ApiKey is not null ? _protector.Unprotect(cfg.ApiKey) : null;
        var secret = cfg.ClientSecret is not null ? _protector.Unprotect(cfg.ClientSecret) : null;
        return (apiKey, cfg.TenantId, cfg.ClientId, secret);
    }

    public async Task<(bool Ok, string Message)> TestConnectionAsync(
        string endpoint, string? agentId,
        string? apiKey,
        string? tenantId, string? clientId, string? clientSecret,
        CancellationToken ct = default)
    {
        try
        {
            PersistentAgentsClient agentsClient = FoundryClientFactory.Build(
                endpoint, apiKey, tenantId, clientId, clientSecret);

            // When an Agent ID is provided, verify it exists.
            // Otherwise list one agent — lightweight call that confirms credentials + endpoint are valid.
            if (!string.IsNullOrWhiteSpace(agentId))
            {
                var response = await agentsClient.Administration.GetAgentAsync(agentId, ct);
                var name = response?.Value?.Name ?? agentId;
                return (true, $"Connected successfully. Agent '{name}' found.");
            }
            else
            {
                await foreach (var _ in agentsClient.Administration.GetAgentsAsync(cancellationToken: ct))
                    break; // one page item is enough to confirm connectivity

                return (true,
                    "Connected successfully. Endpoint and credentials are valid. " +
                    "The agent will be created automatically on the first chat.");
            }
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 403)
        {
            var isApiKey  = !string.IsNullOrEmpty(apiKey);
            var isAzureML = endpoint.Contains(".api.azureml.ms", StringComparison.OrdinalIgnoreCase);

            if (isApiKey && isAzureML)
                return (false,
                    "Authentication failed (403). AzureML workspace endpoints (*.api.azureml.ms) do not " +
                    "support API key authentication. Switch to Service Principal, or use an Azure AI Services " +
                    "endpoint (*.services.ai.azure.com) where API keys are supported.");

            return (false, isApiKey
                ? "Authentication failed (403). Verify that the API key is correct and that the " +
                  "Foundry resource grants API key access."
                : "Authentication failed (403). Verify that the configured identity (Managed Identity " +
                  "or Service Principal) has the 'Azure AI User' role on the Foundry resource.");
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            var isOpenAI = endpoint.Contains(".openai.azure.com", StringComparison.OrdinalIgnoreCase);

            if (isOpenAI)
                return (false,
                    "Resource not found (404). Azure OpenAI endpoints (*.openai.azure.com) are not " +
                    "compatible with the Azure AI Agents API. Use your Azure AI Foundry project endpoint " +
                    "instead: Azure AI Studio → your project → Overview → 'Project endpoint'.");

            return (false,
                "Resource not found (404). Check: " +
                "(1) The endpoint is your Azure AI Foundry project endpoint (not an Azure OpenAI endpoint). " +
                $"(2) Any Agent ID you provided ({agentId ?? "none"}) exists in the project.");
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 429)
        {
            return (false,
                "Rate limit exceeded (429). The Foundry resource has reached its request quota. " +
                "Please wait a moment and try the test again.");
        }
        catch (Exception ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    // Credential values are never returned to the client — HasApiKey/HasServicePrincipal signal their presence
    private static AgentConfigurationDto ToDto(AgentConfiguration cfg) => new()
    {
        Id = cfg.Id,
        OrgId = cfg.OrgId,
        FoundryEndpoint = cfg.FoundryEndpoint,
        AgentId = cfg.AgentId,
        DisplayName = cfg.DisplayName,
        IsEnabled = cfg.IsEnabled,
        DeploymentName = cfg.DeploymentName,
        HasApiKey = !string.IsNullOrEmpty(cfg.ApiKey),
        HasServicePrincipal = !string.IsNullOrEmpty(cfg.ClientSecret),
        UpdatedAt = cfg.UpdatedAt
    };

    // Client construction delegated to FoundryClientFactory (see Velo.Agent/FoundryClientFactory.cs).
}
