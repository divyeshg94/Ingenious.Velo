namespace Velo.Shared.Models;

public class DoraMetricsDto
{
    public Guid Id { get; set; }
    public string OrgId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public DateTimeOffset ComputedAt { get; set; }
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
