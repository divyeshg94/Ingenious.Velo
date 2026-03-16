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

    public double DeploymentFrequency { get; set; }
    public string DeploymentFrequencyRating { get; set; } = string.Empty;

    public double LeadTimeForChangesHours { get; set; }
    public string LeadTimeRating { get; set; } = string.Empty;

    public double ChangeFailureRate { get; set; }
    public string ChangeFailureRating { get; set; } = string.Empty;

    public double MeanTimeToRestoreHours { get; set; }
    public string MttrRating { get; set; } = string.Empty;

    public double ReworkRate { get; set; }
    public string ReworkRateRating { get; set; } = string.Empty;
}
