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

    /// <summary>Write-only: Azure AD Tenant ID for the service principal. Never returned by GET.</summary>
    public string? TenantId { get; set; }

    /// <summary>Write-only: Service principal Client (App) ID. Never returned by GET.</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Write-only: plaintext client secret when saving. The server encrypts it before storage.
    /// Never populated on GET responses — check <see cref="HasServicePrincipal"/> instead.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// True when service principal credentials are stored for this org.
    /// Returned by GET; allows the UI to show "credentials saved" without exposing them.
    /// </summary>
    public bool HasServicePrincipal { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
