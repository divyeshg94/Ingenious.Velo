using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Velo.SQL.Models;

/// <summary>
/// User feedback submitted through the Velo extension.
/// Scoped by org_id for multi-tenancy.
/// </summary>
[Table("Feedback")]
public class Feedback : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Maps to the row-level org scope (e.g. "my-ado-org").</summary>
    [Required, MaxLength(100)]
    public string OrgId { get; set; } = string.Empty;

    /// <summary>ID of the user who submitted the feedback (from Azure AD token).</summary>
    [MaxLength(256)]
    public string? UserId { get; set; }

    /// <summary>Type of feedback: Bug, FeatureRequest, MetricConcern, PerformanceIssue.</summary>
    [Required, MaxLength(50)]
    public string FeedbackType { get; set; } = string.Empty;

    /// <summary>The feedback message (user-supplied text).</summary>
    [Required, MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    /// <summary>Optional context: current project ID when feedback was submitted.</summary>
    [MaxLength(200)]
    public string? ProjectId { get; set; }

    /// <summary>Tracks whether this feedback has been reviewed/resolved.</summary>
    public bool IsReviewed { get; set; }
}
