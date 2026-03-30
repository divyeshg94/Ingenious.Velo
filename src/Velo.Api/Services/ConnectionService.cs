using Microsoft.EntityFrameworkCore;
using Velo.Api.Interface;
using Velo.SQL;
using Velo.SQL.Models;

namespace Velo.Api.Services;

public class ConnectionService(VeloDbContext db, ILogger<ConnectionService> logger) : IConnectionService
{
    public async Task RegisterAsync(string orgUrl, string personalAccessToken, CancellationToken cancellationToken)
    {
        var orgId = db.CurrentOrgId;
        if (string.IsNullOrEmpty(orgId))
            throw new InvalidOperationException("Org context not set — tenant middleware must run before ConnectionService.");

        var normalizedUrl = orgUrl.TrimEnd('/');
        var displayName = ParseOrgName(normalizedUrl);

        var existing = await db.Organizations
            .FirstOrDefaultAsync(o => o.OrgId == orgId, cancellationToken);

        if (existing != null)
        {
            existing.OrgUrl = normalizedUrl;
            existing.DisplayName = displayName;
            existing.LastSeenAt = DateTimeOffset.UtcNow;
            db.Organizations.Update(existing);
            logger.LogInformation("CONNECTION: Updated org registration — OrgId={OrgId}, OrgUrl={OrgUrl}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(normalizedUrl));
        }
        else
        {
            db.Organizations.Add(new OrgContext
            {
                OrgId = orgId,
                OrgUrl = normalizedUrl,
                DisplayName = displayName,
                IsPremium = false,
                DailyTokenBudget = 50_000,
                RegisteredAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow
            });
            logger.LogInformation("CONNECTION: Registered new org — OrgId={OrgId}, OrgUrl={OrgUrl}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(normalizedUrl));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        var orgId = db.CurrentOrgId;
        if (string.IsNullOrEmpty(orgId))
            throw new InvalidOperationException("Org context not set — tenant middleware must run before ConnectionService.");

        var existing = await db.Organizations
            .FirstOrDefaultAsync(o => o.OrgId == orgId, cancellationToken);

        if (existing == null)
        {
            logger.LogWarning("CONNECTION: Remove requested but org not found — OrgId={OrgId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));
            return;
        }

        db.Organizations.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("CONNECTION: Removed org registration — OrgId={OrgId}",
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));
    }

    // Extracts "mycompany" from "https://dev.azure.com/mycompany"
    private static string ParseOrgName(string orgUrl)
    {
        if (Uri.TryCreate(orgUrl, UriKind.Absolute, out var uri))
            return uri.Segments.LastOrDefault()?.Trim('/') ?? orgUrl;
        return orgUrl;
    }
}
