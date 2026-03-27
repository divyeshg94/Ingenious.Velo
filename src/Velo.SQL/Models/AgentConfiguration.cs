using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Velo.SQL.Models;

/// <summary>
/// Stores per-org Azure AI Foundry agent configuration.
/// One row per organisation — the endpoint and agent ID together define which
/// Foundry deployment backs the Velo AI Agent chat for that tenant.
/// </summary>
[Table("AgentConfigurations")]
public class AgentConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Maps to the row-level org scope (e.g. "my-ado-org").</summary>
    [Required, MaxLength(100)]
    public string OrgId { get; set; } = string.Empty;

    /// <summary>
    /// Azure AI Foundry project endpoint URL.
    /// Example: https://my-project.api.azureml.ms/
    /// </summary>
    [Required, MaxLength(500)]
    public string FoundryEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// The Foundry agent ID (e.g. asst_xxxx).
    /// Null when the agent has not yet been auto-created. VeloAgent sets this on first use
    /// via IAgentDataProvider.SaveAgentIdAsync so subsequent calls reuse the same agent.
    /// </summary>
    [MaxLength(200)]
    public string? AgentId { get; set; }

    /// <summary>Optional friendly display name shown in the chat header.</summary>
    [MaxLength(200)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Azure OpenAI model deployment name used when auto-creating the Foundry agent.
    /// Defaults to "gpt-4o". Must match a deployment that exists in the Foundry project.
    /// </summary>
    [MaxLength(100)]
    public string DeploymentName { get; set; } = "gpt-4o";

    /// <summary>Allows admins to disable the agent without deleting the config.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Authentication credentials — exactly one method should be populated per org.
    /// Priority used by VeloAgent: API key → Service Principal → Managed Identity.
    /// All values are encrypted via ASP.NET Core Data Protection before storage.
    /// Never returned to the client — use HasApiKey / HasServicePrincipal on the DTO.
    /// </summary>

    // ── Option 1: API key ──────────────────────────────────────────────────────
    [MaxLength(1000)]
    public string? ApiKey { get; set; }

    // ── Option 2: Service principal (cross-tenant customer Foundry resource) ───
    [MaxLength(200)]
    public string? TenantId { get; set; }

    [MaxLength(200)]
    public string? ClientId { get; set; }

    /// <summary>Encrypted client secret — max length allows for Data Protection overhead.</summary>
    [MaxLength(1000)]
    public string? ClientSecret { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
