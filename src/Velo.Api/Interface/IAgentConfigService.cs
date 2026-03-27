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
    /// </summary>
    Task<(bool Ok, string Message)> TestConnectionAsync(string endpoint, string? agentId, string? apiKey, CancellationToken ct = default);

    /// <summary>
    /// Returns the decrypted API key for internal agent use. Never exposed to the client.
    /// </summary>
    Task<string?> GetDecryptedApiKeyAsync(string orgId, CancellationToken ct = default);
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

        // Only overwrite the stored API key when the client sends a new value.
        // Sending null / "" means "keep the existing key".
        if (!string.IsNullOrWhiteSpace(dto.ApiKey))
            existing.ApiKey = _protector.Protect(dto.ApiKey.Trim());

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

    public async Task<string?> GetDecryptedApiKeyAsync(string orgId, CancellationToken ct = default)
    {
        var cfg = await db.AgentConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.OrgId == orgId, ct);

        if (cfg is null || cfg.ApiKey is null) return null;

        return _protector.Unprotect(cfg.ApiKey);
    }

    public async Task<(bool Ok, string Message)> TestConnectionAsync(
        string endpoint, string? agentId, string? apiKey,
        CancellationToken ct = default)
    {
        try
        {
            PersistentAgentsClient agentsClient = !string.IsNullOrWhiteSpace(apiKey)
                ? new PersistentAgentsClient(endpoint, new ApiKeyTokenCredential(apiKey))
                : new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential()).GetPersistentAgentsClient();

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
                // List agents to confirm connectivity — auto-create will happen on first chat
                var agents = agentsClient.Administration.GetAgents();
                return (true, "Connected successfully. Agent will be created automatically on first chat.");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    // The API key is never returned to the client — HasApiKey signals its presence
    private static AgentConfigurationDto ToDto(AgentConfiguration cfg) => new()
    {
        Id = cfg.Id,
        OrgId = cfg.OrgId,
        FoundryEndpoint = cfg.FoundryEndpoint,
        AgentId = cfg.AgentId,
        DisplayName = cfg.DisplayName,
        IsEnabled = cfg.IsEnabled,
        HasApiKey = !string.IsNullOrEmpty(cfg.ApiKey),
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
