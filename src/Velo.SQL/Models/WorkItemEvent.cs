using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Velo.SQL.Models;

/// <summary>
/// Stores ADO workitem.updated state-transition events for rework rate computation.
///
/// A "rework" transition is when a work item moves FROM a completed state
/// (Resolved, Closed, Done, …) BACK TO an active state (Active, In Progress, …),
/// indicating that a previously finished item required additional work.
///
/// Rework Rate = rework transitions ÷ total "done" transitions × 100
/// </summary>
[Table("WorkItemEvents")]
public class WorkItemEvent : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string OrgId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>ADO work item numeric ID (resource.workItemId).</summary>
    public int WorkItemId { get; set; }

    /// <summary>Work item type: Bug, Task, User Story, Feature, etc.</summary>
    [MaxLength(100)]
    public string? WorkItemType { get; set; }

    /// <summary>State before the change (System.State oldValue).</summary>
    [MaxLength(100)]
    public string? OldState { get; set; }

    /// <summary>State after the change (System.State newValue).</summary>
    [MaxLength(100)]
    public string? NewState { get; set; }

    /// <summary>Timestamp of the state change (resource.revisedDate).</summary>
    public DateTimeOffset ChangedAt { get; set; }

    public DateTimeOffset IngestedAt { get; set; } = DateTimeOffset.UtcNow;
}
