using Azure.AI.Agents.Persistent;
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
    Task<(bool Ok, string Message)> TestConnectionAsync(string endpoint, string agentId, string? tenantId, string? clientId, string? clientSecret, CancellationToken ct = default);

    /// <summary>
    /// Returns decrypted service principal credentials for internal agent use. Never exposed to the client.
    /// </summary>
    Task<(string? TenantId, string? ClientId, string? ClientSecret)> GetDecryptedCredentialsAsync(string orgId, CancellationToken ct = default);
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
        existing.AgentId = dto.AgentId.Trim();
        existing.DisplayName = dto.DisplayName?.Trim();
        existing.IsEnabled = dto.IsEnabled;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        // Only overwrite stored credentials when the client sends new values.
        // Sending null / "" means "keep the existing credentials".
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

    public async Task<(string? TenantId, string? ClientId, string? ClientSecret)> GetDecryptedCredentialsAsync(
        string orgId, CancellationToken ct = default)
    {
        var cfg = await db.AgentConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.OrgId == orgId, ct);

        if (cfg is null) return (null, null, null);

        var secret = cfg.ClientSecret is not null ? _protector.Unprotect(cfg.ClientSecret) : null;
        return (cfg.TenantId, cfg.ClientId, secret);
    }

    public async Task<(bool Ok, string Message)> TestConnectionAsync(
        string endpoint, string agentId,
        string? tenantId, string? clientId, string? clientSecret,
        CancellationToken ct = default)
    {
        try
        {
            Azure.Core.TokenCredential credential;
            if (!string.IsNullOrWhiteSpace(tenantId)
                && !string.IsNullOrWhiteSpace(clientId)
                && !string.IsNullOrWhiteSpace(clientSecret))
            {
                credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            }
            else
            {
                credential = new DefaultAzureCredential();
            }

            var projectClient = new AIProjectClient(new Uri(endpoint), credential);
            var agentsClient = projectClient.GetPersistentAgentsClient();

            var agent = agentsClient.Administration.GetAgent(agentId);
            var name = agent?.Value?.Name ?? agentId;
            return (true, $"Connected successfully to agent '{name}'.");
        }
        catch (Exception ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    // Service principal values are never returned to the client — HasServicePrincipal signals their presence
    private static AgentConfigurationDto ToDto(AgentConfiguration cfg) => new()
    {
        Id = cfg.Id,
        OrgId = cfg.OrgId,
        FoundryEndpoint = cfg.FoundryEndpoint,
        AgentId = cfg.AgentId,
        DisplayName = cfg.DisplayName,
        IsEnabled = cfg.IsEnabled,
        HasServicePrincipal = !string.IsNullOrEmpty(cfg.ClientSecret),
        UpdatedAt = cfg.UpdatedAt
    };
}
