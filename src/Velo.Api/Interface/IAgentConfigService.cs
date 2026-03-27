using Azure;
using Azure.AI.Projects;
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
    Task<(bool Ok, string Message)> TestConnectionAsync(string endpoint, string agentId, string? apiKey, CancellationToken ct = default);

    /// <summary>
    /// Returns the decrypted API key for internal agent use. Never exposed to the client.
    /// </summary>
    Task<string?> GetDecryptedApiKeyAsync(string orgId, CancellationToken ct = default);
}

public class AgentConfigService(VeloDbContext db, IDataProtectionProvider dataProtection) : IAgentConfigService
{
    // Purpose-limited protector — key material is isolated to this use case
    private readonly IDataProtector _protector =
        dataProtection.CreateProtector("Velo.AgentConfig.ApiKey.v1");

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
        existing.AgentId = dto.AgentId.Trim();
        existing.DisplayName = dto.DisplayName?.Trim();
        existing.IsEnabled = dto.IsEnabled;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        // Only overwrite the stored key when the client sends a new one.
        // Sending ApiKey = null / "" means "keep the existing key".
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

        if (cfg?.ApiKey is null) return null;

        return _protector.Unprotect(cfg.ApiKey);
    }

    public async Task<(bool Ok, string Message)> TestConnectionAsync(
        string endpoint, string agentId, string? apiKey, CancellationToken ct = default)
    {
        try
        {
            AgentsClient agentsClient;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                agentsClient = new AgentsClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            }
            else
            {
                var projectClient = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
                agentsClient = projectClient.GetAgentsClient();
            }

            var agent = await agentsClient.GetAgentAsync(agentId, ct);
            var name = agent.Value?.Name ?? agentId;
            return (true, $"Connected successfully to agent '{name}'.");
        }
        catch (Exception ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    // API key is never returned to the client — HasApiKey signals its presence
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
}
