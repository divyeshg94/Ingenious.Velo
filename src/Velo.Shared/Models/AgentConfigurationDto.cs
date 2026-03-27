namespace Velo.Shared.Models;

/// <summary>API-facing DTO for per-org Foundry agent configuration.</summary>
public class AgentConfigurationDto
{
    /// <summary>Empty GUID for new records; populated by the server after save.</summary>
    public Guid Id { get; set; } = Guid.Empty;

    public string OrgId { get; set; } = string.Empty;

    /// <summary>Azure AI Foundry project endpoint URL.</summary>
    public string FoundryEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Foundry agent ID (e.g. asst_xxxx). Optional — null means the agent will be
    /// auto-created by Velo on first chat request and persisted automatically.
    /// </summary>
    public string? AgentId { get; set; }

    public string? DisplayName { get; set; }

    /// <summary>
    /// Azure OpenAI model deployment name (e.g. "gpt-4o", "gpt-4o-mini").
    /// Used when Velo auto-creates the Foundry agent on first chat.
    /// </summary>
    public string DeploymentName { get; set; } = "gpt-4o";

    public bool IsEnabled { get; set; } = true;

    // ── Option 1: API key ──────────────────────────────────────────────────────

    /// <summary>
    /// Write-only: plaintext API key when saving. The server encrypts it before storage.
    /// Never populated on GET responses — check <see cref="HasApiKey"/> instead.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>True when an encrypted API key is stored for this org.</summary>
    public bool HasApiKey { get; set; }

    // ── Option 2: Service principal ────────────────────────────────────────────

    /// <summary>Write-only: Azure AD Tenant ID for the service principal. Never returned by GET.</summary>
    public string? TenantId { get; set; }

    /// <summary>Write-only: Service principal Client (App) ID. Never returned by GET.</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Write-only: plaintext client secret when saving. The server encrypts it before storage.
    /// Never populated on GET responses — check <see cref="HasServicePrincipal"/> instead.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>True when encrypted service principal credentials are stored for this org.</summary>
    public bool HasServicePrincipal { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
