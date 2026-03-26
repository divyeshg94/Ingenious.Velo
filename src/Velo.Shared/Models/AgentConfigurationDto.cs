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

    public DateTimeOffset? UpdatedAt { get; set; }
}
