namespace Velo.Shared.Models;

/// <summary>
/// DTO for a work item state-transition event captured from the
/// ADO workitem.updated service hook.
/// </summary>
public class WorkItemEventDto
{
    public Guid Id { get; set; }
    public string OrgId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public int WorkItemId { get; set; }
    public string? WorkItemType { get; set; }
    /// <summary>State before the transition.</summary>
    public string? OldState { get; set; }
    /// <summary>State after the transition.</summary>
    public string? NewState { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
}
