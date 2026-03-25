using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Velo.SQL.Models;

/// <summary>
/// Maps a repository name to a friendly team name within a project.
/// Allows teams to label their repositories (e.g. "backend-api" → "Platform Team").
/// One repo can only belong to one team per project.
/// </summary>
[Table("TeamMappings")]
public class TeamMapping : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string OrgId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>ADO repository name (exact match, case-insensitive comparison in queries).</summary>
    [Required, MaxLength(200)]
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>Friendly team name shown in the UI (e.g. "Platform Team", "Frontend").</summary>
    [Required, MaxLength(200)]
    public string TeamName { get; set; } = string.Empty;
}
