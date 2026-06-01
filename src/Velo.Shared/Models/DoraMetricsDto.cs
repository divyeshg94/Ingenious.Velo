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

    /// <summary>
    /// True when no deployment-tagged pipelines were detected and the metric
    /// was computed from all successful runs as a fallback estimate.
    /// </summary>
    public bool IsDeploymentFrequencyEstimated { get; set; }

    /// <summary>
    /// Average pipeline build duration (hours) of successful runs.
    /// Always an approximation — true lead time requires PR-merge-to-deploy linkage.
    /// </summary>
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
    /// True when no deployment-tagged pipelines were detected and Change Failure Rate
    /// was computed from all pipeline runs as a fallback (which conflates build failures
    /// with production deployment failures).
    /// </summary>
    public bool IsChangeFailureRateEstimated { get; set; }

    public double MeanTimeToRestoreHours { get; set; }
    public string MttrRating { get; set; } = string.Empty;

    /// <summary>
    /// True when no deployment-tagged pipelines were detected and MTTR was computed
    /// from all pipeline failures as a fallback (which may include non-production failures).
    /// </summary>
    public bool IsMttrEstimated { get; set; }
    public double ReworkRate { get; set; }
    public string ReworkRateRating { get; set; } = string.Empty;

    /// <summary>
    /// True when work-item event data was unavailable for the period and Rework Rate
    /// defaulted to 0 (insufficient data) rather than a measured value.
    /// </summary>
    public bool IsReworkRateEstimated { get; set; }
}
