using Microsoft.EntityFrameworkCore;
using Velo.SQL;

namespace Velo.Api.Services;

/// <summary>
/// Tracks the three "first value" onboarding milestones a new org needs to hit to see
/// Velo actually working: sync one pipeline, view one DORA metric, share a link.
/// Read within a normal authenticated request, so the caller's scoped VeloDbContext
/// already has CurrentOrgId (and SQL Server RLS session context) set by
/// TenantResolutionMiddleware — no tenant plumbing needed here.
/// </summary>
public interface IOnboardingService
{
    Task<OnboardingProgressDto> GetProgressAsync(string orgId, CancellationToken cancellationToken);

    /// <summary>Marks the "viewed a DORA metric" milestone, once, the first time it happens.</summary>
    Task MarkDoraViewedAsync(string orgId, CancellationToken cancellationToken);

    /// <summary>Marks the "shared a dashboard link" milestone, once, the first time it happens.</summary>
    Task MarkShareCreatedAsync(string orgId, CancellationToken cancellationToken);
}

public record OnboardingProgressDto(
    bool HasSyncedPipeline,
    bool HasViewedDoraMetric,
    bool HasSharedDashboard,
    DateTimeOffset? RegisteredAt)
{
    public bool IsComplete => HasSyncedPipeline && HasViewedDoraMetric && HasSharedDashboard;
}

public class OnboardingService(VeloDbContext db, ILogger<OnboardingService> logger) : IOnboardingService
{
    public async Task<OnboardingProgressDto> GetProgressAsync(string orgId, CancellationToken cancellationToken)
    {
        var org = await db.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrgId == orgId, cancellationToken);

        // PipelineRuns carries the standard tenant query filter, so this only ever
        // sees the current org's rows.
        var hasSyncedPipeline = await db.PipelineRuns.AsNoTracking().AnyAsync(cancellationToken);

        return new OnboardingProgressDto(
            HasSyncedPipeline: hasSyncedPipeline,
            HasViewedDoraMetric: org?.FirstDoraViewedAt != null,
            HasSharedDashboard: org?.FirstShareCreatedAt != null,
            RegisteredAt: org?.RegisteredAt);
    }

    public async Task MarkDoraViewedAsync(string orgId, CancellationToken cancellationToken)
    {
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.OrgId == orgId, cancellationToken);
        if (org == null || org.FirstDoraViewedAt != null) return;

        org.FirstDoraViewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Onboarding milestone reached: first DORA view — OrgId: {OrgId}",
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));
    }

    public async Task MarkShareCreatedAsync(string orgId, CancellationToken cancellationToken)
    {
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.OrgId == orgId, cancellationToken);
        if (org == null || org.FirstShareCreatedAt != null) return;

        org.FirstShareCreatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Onboarding milestone reached: first share created — OrgId: {OrgId}",
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));
    }
}
