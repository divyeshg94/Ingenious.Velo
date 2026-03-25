using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Velo.SQL.Models;

/// <summary>
/// Maps an ADO project GUID to its human-readable name, per organisation.
/// ADO service hook payloads always carry the project GUID; this table is populated
/// during sync so that webhooks can resolve the name without an ADO token.
/// </summary>
[Table("ProjectMappings")]
public class ProjectMapping
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string OrgId { get; set; } = string.Empty;

    /// <summary>ADO project GUID (from resourceContainers.project.id).</summary>
    [Required, MaxLength(100)]
    public string ProjectGuid { get; set; } = string.Empty;

    /// <summary>Human-readable project name (from ADO projects API response).</summary>
    [Required, MaxLength(200)]
    public string ProjectName { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
