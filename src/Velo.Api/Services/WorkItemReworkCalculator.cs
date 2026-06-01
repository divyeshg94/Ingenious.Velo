using Velo.Shared.Models;

namespace Velo.Api.Services;

/// <summary>
/// Shared work-item rework-rate computation used by both DoraComputeService and TeamHealthComputeService.
///
/// Implements a proxy for the DORA Rework Rate metric:
///   Rework Rate = Unplanned Deployments ÷ Total Deployments
///   (see https://dora.dev/guides/dora-metrics/)
///
/// Since unplanned deployment tagging is not always available, this uses work-item
/// state-transition churn as a proxy:
///   rework = items that transitioned FROM a done state BACK TO an active state
///   ÷ total completions (active → done transitions), expressed as a percentage.
///
/// Benchmarks (DORA): Elite ≤4%, High ≤8%, Medium ≤32%, Low >32%
///
/// Formula: reworkTransitions ÷ totalCompletions × 100  (capped at 100 %)
///
/// Returns 0 when totalCompletions == 0 (insufficient data).
/// </summary>
internal static class WorkItemReworkCalculator
{
    internal static readonly HashSet<string> DoneStates = new(StringComparer.OrdinalIgnoreCase)
        { "Resolved", "Closed", "Done", "Completed", "Inactive", "Verified" };

    internal static readonly HashSet<string> ActiveStates = new(StringComparer.OrdinalIgnoreCase)
        { "Active", "In Progress", "Committed", "Open", "New", "Reopened" };

    /// <summary>
    /// Computes rework rate from a list of work item state-transition events.
    /// Returns 0 when no completions exist (no data).
    /// </summary>
    public static double Compute(IReadOnlyList<WorkItemEventDto> events)
    {
        var reworkTransitions = events.Count(e =>
            DoneStates.Contains(e.OldState ?? string.Empty) &&
            ActiveStates.Contains(e.NewState ?? string.Empty));

        var totalCompletions = events.Count(e =>
            ActiveStates.Contains(e.OldState ?? string.Empty) &&
            DoneStates.Contains(e.NewState ?? string.Empty));

        if (totalCompletions == 0) return 0;
        return Math.Min((double)reworkTransitions / totalCompletions * 100, 100);
    }
}
