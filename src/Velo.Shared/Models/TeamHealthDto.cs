namespace Velo.Shared.Models;

public class TeamHealthDto
{
    public Guid Id { get; set; }
    public string OrgId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public DateTimeOffset ComputedAt { get; set; }

    /// <summary>
    /// Filter context for the snapshot. Empty string = project-wide aggregate.
    /// A repo name = single-repo snapshot. "team:&lt;TeamName&gt;" = team snapshot.
    /// </summary>
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
