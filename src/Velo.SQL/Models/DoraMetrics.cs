using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Velo.SQL.Models;

[Table("DoraMetrics")]
public class DoraMetrics : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string OrgId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ProjectId { get; set; } = string.Empty;

    public DateTimeOffset ComputedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }

    /// <summary>
    /// UTC date bucket for the metric snapshot. Backs the unique index
    /// (OrgId, ProjectId, ComputedDate) so concurrent recomputes cannot
    /// insert duplicate per-day rows.
    /// </summary>
    [Column(TypeName = "date")]
    public DateTime ComputedDate { get; set; }

    /// <summary>
    /// Filter context for the snapshot. Empty string ("") = project-wide aggregate.
    /// A literal repository name = single-repo snapshot. "team:&lt;TeamName&gt;" = team
    /// snapshot resolved via TeamMappings to multiple repos. Stored as part of the
    /// composite UNIQUE index UX_DoraMetrics_OrgId_ProjectId_ComputedDate_RepositoryName
    /// so per-filter daily snapshots don't collide with the project-wide row.
    /// Empty string sentinel chosen so SQL Server's standard unique-index NULL semantics
    /// don't break the upsert path.
    /// </summary>
    [Required, MaxLength(200)]
    public string RepositoryName { get; set; } = string.Empty;

    public double DeploymentFrequency { get; set; }
    public string DeploymentFrequencyRating { get; set; } = string.Empty;

    /// <summary>
    /// True when no deployment-tagged pipelines were detected and the metric
    /// was computed from all successful runs as a fallback estimate.
    /// </summary>
    public bool IsDeploymentFrequencyEstimated { get; set; }

    public double LeadTimeForChangesHours { get; set; }
    public string LeadTimeRating { get; set; } = string.Empty;

    /// <summary>
    /// Always true: Lead Time is currently computed as average pipeline build duration,
    /// not as PR-merge-to-deploy time.
    /// </summary>
    public bool IsLeadTimeApproximate { get; set; }

    public double ChangeFailureRate { get; set; }
    public string ChangeFailureRating { get; set; } = string.Empty;

    /// <summary>
    /// True when no deployment-tagged pipelines were detected and CFR was computed
    /// from all pipeline runs as a fallback estimate.
    /// </summary>
    public bool IsChangeFailureRateEstimated { get; set; }

    public double MeanTimeToRestoreHours { get; set; }
    public string MttrRating { get; set; } = string.Empty;

    /// <summary>
    /// True when no deployment-tagged pipelines were detected and MTTR was computed
    /// from all pipeline failures as a fallback (may include non-production failures).
    /// </summary>
    public bool IsMttrEstimated { get; set; }

    public double ReworkRate { get; set; }
    public string ReworkRateRating { get; set; } = string.Empty;

    /// <summary>
    /// True when work-item event data was unavailable and Rework Rate
    /// defaulted to 0 (insufficient data).
    /// </summary>
    public bool IsReworkRateEstimated { get; set; }
}
