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
    /// The Agent ID created in Azure AI Studio.
    /// Example: asst_xxxxxxxxxxxxxxxxxxxxxxxx
    /// </summary>
    [Required, MaxLength(200)]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>Optional friendly display name shown in the chat header.</summary>
    [MaxLength(200)]
    public string? DisplayName { get; set; }

    /// <summary>Allows admins to disable the agent without deleting the config.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// ASP.NET Core Data Protection-encrypted API key from Azure AI Studio.
    /// Null when the org relies on Velo's Managed Identity instead.
    /// Never returned to the client — use HasApiKey on the DTO.
    /// </summary>
    [MaxLength(1000)]
    public string? ApiKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
