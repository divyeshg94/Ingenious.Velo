namespace Velo.Shared.Models;

/// <summary>API-facing DTO for per-org Foundry agent configuration.</summary>
public class AgentConfigurationDto
{
    /// <summary>Empty GUID for new records; populated by the server after save.</summary>
    public Guid Id { get; set; } = Guid.Empty;

    public string OrgId { get; set; } = string.Empty;

    /// <summary>Azure AI Foundry project endpoint URL.</summary>
    public string FoundryEndpoint { get; set; } = string.Empty;

    /// <summary>Agent ID from Azure AI Studio.</summary>
    public string AgentId { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Write-only: supply a plaintext API key when saving. The server encrypts it before storage.
    /// Never populated on GET responses — check <see cref="HasApiKey"/> instead.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// True when an encrypted API key is stored for this org.
    /// Returned by GET; allows the UI to show "key saved" without exposing the value.
    /// </summary>
    public bool HasApiKey { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
