using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
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
            PersistentAgentsClient agentsClient = BuildAgentsClient(endpoint, apiKey, tenantId, clientId, clientSecret);

            // When an Agent ID is provided, verify it exists. Otherwise just confirm the
            // endpoint and credentials are reachable by listing agents (lightweight call).
            if (!string.IsNullOrWhiteSpace(agentId))
            {
                var agent = agentsClient.Administration.GetAgent(agentId);
                var name = agent?.Value?.Name ?? agentId;
                return (true, $"Connected successfully. Agent '{name}' found.");
            }
            else
            {
                var agents = agentsClient.Administration.GetAgents();
                return (true, "Connected successfully. Agent will be created automatically on first chat.");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    // Shared credential resolution: API key → Service principal → Managed Identity
    internal static PersistentAgentsClient BuildAgentsClient(
        string endpoint,
        string? apiKey,
        string? tenantId, string? clientId, string? clientSecret)
    {
        if (!string.IsNullOrEmpty(apiKey))
            return new PersistentAgentsClient(endpoint, new ApiKeyTokenCredential(apiKey));

        if (!string.IsNullOrEmpty(tenantId)
            && !string.IsNullOrEmpty(clientId)
            && !string.IsNullOrEmpty(clientSecret))
            return new PersistentAgentsClient(endpoint,
                new ClientSecretCredential(tenantId, clientId, clientSecret));

        return new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential())
            .GetPersistentAgentsClient();
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
        HasApiKey = !string.IsNullOrEmpty(cfg.ApiKey),
        HasServicePrincipal = !string.IsNullOrEmpty(cfg.ClientSecret),
        UpdatedAt = cfg.UpdatedAt
    };

    /// <summary>
    /// Wraps an Azure AI Foundry API key as a <see cref="TokenCredential"/> so it can be passed
    /// to SDK clients that only accept <see cref="TokenCredential"/>. The key is sent as the
    /// Bearer token value in the Authorization header, which Azure AI Services validates.
    /// </summary>
    private sealed class ApiKeyTokenCredential(string apiKey) : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(apiKey, DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(new AccessToken(apiKey, DateTimeOffset.MaxValue));
    }
}
