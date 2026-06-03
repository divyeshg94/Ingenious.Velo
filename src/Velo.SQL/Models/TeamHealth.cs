using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Velo.SQL.Models;

[Table("TeamHealth")]
public class TeamHealth : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string OrgId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ProjectId { get; set; } = string.Empty;

    public DateTimeOffset ComputedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Filter context for the snapshot. Empty string ("") = project-wide aggregate.
    /// A repository name = single-repo snapshot. "team:&lt;TeamName&gt;" = team snapshot
    /// resolved via TeamMappings. Used by the dashboard to look up the right slice.
    /// </summary>
    [Required, MaxLength(200)]
    public string RepositoryName { get; set; } = string.Empty;

    public double CodingTimeHours { get; set; }
    public double ReviewTimeHours { get; set; }
    public double MergeTimeHours { get; set; }
    public double DeployTimeHours { get; set; }

    public double AveragePrSizeLines { get; set; }
    public double PrCommentDensity { get; set; }
    public double PrApprovalRate { get; set; }

    public double TestPassRate { get; set; }
    public double FlakyTestRate { get; set; }

    public double DeploymentRiskScore { get; set; }
}
