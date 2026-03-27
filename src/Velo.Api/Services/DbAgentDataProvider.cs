using Microsoft.EntityFrameworkCore;
using System.Text;
using Velo.Agent;
using Velo.SQL;

namespace Velo.Api.Services;

/// <summary>
/// Implements IAgentDataProvider by querying the Velo SQL database via EF Core.
/// Produces formatted context strings that VeloAgent injects into the Foundry agent prompt.
/// </summary>
public class DbAgentDataProvider(VeloDbContext db) : IAgentDataProvider
{
    public async Task<string> GetPipelineContextAsync(string orgId, string projectId, CancellationToken ct = default)
    {
        db.CurrentOrgId = orgId;

        var runs = await db.PipelineRuns
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.StartTime)
            .Take(20)
            .ToListAsync(ct);

        if (runs.Count == 0)
            return "No pipeline run data available yet. Trigger a sync from the Connections tab to populate data.";

        var sb = new StringBuilder();
        sb.AppendLine($"Last {runs.Count} pipeline runs:");

        foreach (var run in runs)
        {
            var duration = run.DurationMs.HasValue ? $"{run.DurationMs / 1000.0:F0}s" : "N/A";
            sb.AppendLine($"  [{run.StartTime:yyyy-MM-dd HH:mm}] {run.PipelineName} | Result: {run.Result} | Duration: {duration} | Repo: {run.RepositoryName ?? "N/A"} | Deployment: {(run.IsDeployment ? "Yes" : "No")}");
        }

        var successRate = runs.Count == 0 ? 0.0
            : (double)runs.Count(r => string.Equals(r.Result, "succeeded", StringComparison.OrdinalIgnoreCase)) / runs.Count * 100;

        sb.AppendLine($"Overall success rate (last {runs.Count} runs): {successRate:F1}%");

        return sb.ToString();
    }

    public async Task<string> GetDoraContextAsync(string orgId, string projectId, CancellationToken ct = default)
    {
        db.CurrentOrgId = orgId;

        var metrics = await db.DoraMetrics
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderByDescending(m => m.ComputedAt)
            .FirstOrDefaultAsync(ct);

        if (metrics is null)
            return "No DORA metrics available yet. Metrics are computed automatically after pipeline runs are ingested.";

        return $"""
            DORA Metrics (computed {metrics.ComputedAt:yyyy-MM-dd HH:mm} UTC):
              Deployment Frequency:  {metrics.DeploymentFrequency:F2}/day  [{metrics.DeploymentFrequencyRating}]
              Lead Time for Changes: {metrics.LeadTimeForChangesHours:F1} hours  [{metrics.LeadTimeRating}]
              Change Failure Rate:   {metrics.ChangeFailureRate:F1}%  [{metrics.ChangeFailureRating}]
              Mean Time to Restore:  {metrics.MeanTimeToRestoreHours:F1} hours  [{metrics.MttrRating}]
              Rework Rate:           {metrics.ReworkRate:F1}%  [{metrics.ReworkRateRating}]
            """;
    }

    public async Task<string> GetPrContextAsync(string orgId, string projectId, CancellationToken ct = default)
    {
        db.CurrentOrgId = orgId;

        var prs = await db.PullRequestEvents
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(15)
            .ToListAsync(ct);

        if (prs.Count == 0)
            return "No pull request data available yet.";

        var sb = new StringBuilder();
        sb.AppendLine($"Last {prs.Count} pull request events:");

        foreach (var pr in prs)
        {
            var approved = pr.IsApproved ? "Yes" : "No";
            sb.AppendLine($"  [{pr.CreatedAt:yyyy-MM-dd}] PR #{pr.PrId} | Status: {pr.Status} | Reviewers: {pr.ReviewerCount} | Approved: {approved} | Branch: {pr.SourceBranch ?? "N/A"} → {pr.TargetBranch ?? "N/A"}");
        }

        return sb.ToString();
    }

    public async Task SaveAgentIdAsync(string orgId, string agentId, CancellationToken ct = default)
    {
        db.CurrentOrgId = orgId;

        var cfg = await db.AgentConfigurations
            .FirstOrDefaultAsync(a => a.OrgId == orgId, ct);

        if (cfg is null) return; // config must exist (it was just used to get here)

        cfg.AgentId = agentId;
        cfg.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
