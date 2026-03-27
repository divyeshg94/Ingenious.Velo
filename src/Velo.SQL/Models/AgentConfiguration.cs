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

    /// <summary>Allows admins to disable the agent without deleting the config.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Optional API key for authenticating against the Azure AI Foundry endpoint.
    /// Encrypted via ASP.NET Core Data Protection before storage.
    /// Null when the org relies on Velo's Managed Identity instead.
    /// Never returned to the client — use HasApiKey on the DTO.
    /// </summary>
    [MaxLength(1000)]
    public string? ApiKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
