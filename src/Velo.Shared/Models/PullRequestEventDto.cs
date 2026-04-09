namespace Velo.Shared.Models;

public class PullRequestEventDto
{
    public Guid Id { get; set; }
    public string OrgId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public int PrId { get; set; }
    public string? Title { get; set; }
    /// <summary>active | completed | abandoned</summary>
    public string Status { get; set; } = string.Empty;
    public string? SourceBranch { get; set; }
    public string? TargetBranch { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public bool IsApproved { get; set; }
    public int ReviewerCount { get; set; }
    public DateTimeOffset IngestedAt { get; set; }

    // Phase 2: PR Diff Metrics
    public int FilesChanged { get; set; }
    public int LinesAdded { get; set; }
    public int LinesDeleted { get; set; }
    public string? ReviewerNames { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
    public DateTimeOffset? FirstApprovedAt { get; set; }
    public int? CycleDurationMinutes { get; set; }
}
