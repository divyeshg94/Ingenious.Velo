using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Velo.SQL.Models;

[Table("TeamHealth")]
public class TeamHealth
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string OrgId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ProjectId { get; set; } = string.Empty;

    public DateTimeOffset ComputedAt { get; set; } = DateTimeOffset.UtcNow;

    // Cycle time breakdown (hours)
    public double CodingTimeHours { get; set; }
    public double ReviewTimeHours { get; set; }
    public double MergeTimeHours { get; set; }
    public double DeployTimeHours { get; set; }

    // PR quality
    public double AveragePrSizeLines { get; set; }
    public double PrCommentDensity { get; set; }
    public double PrApprovalRate { get; set; }

    // Test health
    public double TestPassRate { get; set; }
    public double FlakyTestRate { get; set; }

    // Deployment risk
    public double DeploymentRiskScore { get; set; } // 0.0 = low risk, 1.0 = high risk
}
