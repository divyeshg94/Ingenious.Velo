namespace Velo.Shared.Models;

public class TeamHealthDto
{
    public Guid Id { get; set; }
    public string OrgId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public DateTimeOffset ComputedAt { get; set; }
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
