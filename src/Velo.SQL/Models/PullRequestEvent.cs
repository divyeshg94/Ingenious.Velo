using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Velo.SQL.Models;

/// <summary>
/// Stores ADO git.pullrequest.created / git.pullrequest.updated service hook events.
/// Used to compute real cycle-time metrics (review time, merge time, approval rate)
/// instead of the pipeline-only proxies used in Phase 1.
/// </summary>
[Table("PullRequestEvents")]
public class PullRequestEvent : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string OrgId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>ADO pull request number (resource.pullRequestId).</summary>
    public int PrId { get; set; }

    [MaxLength(500)]
    public string? Title { get; set; }

    /// <summary>active | completed | abandoned</summary>
    [Required, MaxLength(50)]
    public string Status { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? SourceBranch { get; set; }

    [MaxLength(500)]
    public string? TargetBranch { get; set; }

    /// <summary>resource.creationDate — when the PR was opened.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>resource.closedDate — when the PR was merged or abandoned (null if still open).</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>True when at least one reviewer had vote >= 10 (approved).</summary>
    public bool IsApproved { get; set; }

    public int ReviewerCount { get; set; }

    public DateTimeOffset IngestedAt { get; set; } = DateTimeOffset.UtcNow;
}
