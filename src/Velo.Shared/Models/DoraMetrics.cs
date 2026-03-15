using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Velo.Shared.Models;

[Table("dora_metrics")]
public class DoraMetrics
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

    // Metric 1: Deployment Frequency (deployments/day)
    public double DeploymentFrequency { get; set; }
    public string DeploymentFrequencyRating { get; set; } = string.Empty;

    // Metric 2: Lead Time for Changes (hours)
    public double LeadTimeForChangesHours { get; set; }
    public string LeadTimeRating { get; set; } = string.Empty;

    // Metric 3: Change Failure Rate (percentage 0-100)
    public double ChangeFailureRate { get; set; }
    public string ChangeFailureRating { get; set; } = string.Empty;

    // Metric 4: Mean Time to Restore (hours)
    public double MeanTimeToRestoreHours { get; set; }
    public string MttrRating { get; set; } = string.Empty;

    // Metric 5: Rework Rate (2026 addition, percentage 0-100)
    public double ReworkRate { get; set; }
    public string ReworkRateRating { get; set; } = string.Empty;
}
