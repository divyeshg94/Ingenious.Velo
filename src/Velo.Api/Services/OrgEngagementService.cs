using Microsoft.EntityFrameworkCore;
using Velo.Api.Logging;
using Velo.SQL;

namespace Velo.Api.Services;

/// <summary>
/// Finds orgs that registered but never activated (no pipeline ever synced) and sends
/// their registration-time admin contact a single re-engagement email with an unsubscribe
/// link.
/// AT-MOST-ONCE, not exactly-once: each candidate is claimed with a conditional
/// UPDATE ... WHERE ReEngagementEmailSentAt IS NULL before sending, so two overlapping
/// runs (e.g. this hosted service on two API replicas) can't both win the same org — only
/// the instance whose UPDATE actually changes a row proceeds to send. If the send itself
/// then fails, the claim is deliberately NOT released and the org is not retried: for a
/// marketing email with legal opt-out obligations, silently retrying risks a duplicate
/// send, which is worse than one org missing this campaign. An operator can manually clear
/// <see cref="Velo.SQL.Models.OrgContext.ReEngagementEmailSentAt"/> to retry a specific org.
/// Disabled by default (Marketing:ReEngagementEnabled) — this only sends real email once an
/// operator has reviewed the copy, configured SMTP + an unsubscribe secret, and opted in.
/// </summary>
public interface IOrgEngagementService
{
    /// <summary>Runs one pass of the campaign. Returns the number of emails sent.</summary>
    Task<int> RunNeverUsedOrgCampaignAsync(CancellationToken cancellationToken);
}

public class OrgEngagementService(
    IServiceScopeFactory scopeFactory,
    IEmailService emailService,
    IUnsubscribeTokenService tokenService,
    IConfiguration configuration,
    ILogger<OrgEngagementService> logger) : IOrgEngagementService
{
    public async Task<int> RunNeverUsedOrgCampaignAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Marketing:ReEngagementEnabled", false))
        {
            logger.LogDebug("Never-used-org re-engagement campaign is disabled (Marketing:ReEngagementEnabled).");
            return 0;
        }

        var apiBaseUrl = configuration["Marketing:ApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            logger.LogWarning("Marketing:ApiBaseUrl is not configured — cannot build an unsubscribe link. Campaign skipped.");
            return 0;
        }

        if (!tokenService.IsConfigured)
        {
            logger.LogWarning("Marketing:UnsubscribeSecret is not configured — refusing to send email without a working unsubscribe link. Campaign skipped.");
            return 0;
        }

        if (!emailService.IsConfigured)
        {
            logger.LogWarning("SMTP is not configured — refusing to run the campaign (a claimed org would never actually be emailed). Campaign skipped.");
            return 0;
        }

        var inactivityDays = configuration.GetValue("Marketing:InactivityThresholdDays", 14);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-inactivityDays);

        // Organizations carries no tenant query filter (it IS the tenant table), so this
        // sees every org regardless of which one (if any) is "current".
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VeloDbContext>();

        var candidates = await db.Organizations
            .AsNoTracking()
            .Where(o => !o.IsDeleted
                && o.RegisteredAt <= cutoff
                && !o.MarketingOptOut
                && o.ReEngagementEmailSentAt == null
                && o.AdminContactEmail != null && o.AdminContactEmail != "")
            .ToListAsync(cancellationToken);

        var sent = 0;

        foreach (var org in candidates)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                // Fresh scope per org: TenantContextHelper opens/sets SQL Server RLS
                // session context on the connection it's given, and correctness here
                // depends on that being scoped to exactly one org at a time.
                await using var orgScope = scopeFactory.CreateAsyncScope();
                var orgDb = orgScope.ServiceProvider.GetRequiredService<VeloDbContext>();
                await TenantContextHelper.SetAsync(orgDb, org.OrgId, cancellationToken);

                var everSynced = await orgDb.PipelineRuns.AsNoTracking().AnyAsync(cancellationToken);
                if (everSynced) continue; // org has activated — not a re-engagement target

                // Atomic claim: only the caller whose UPDATE actually changes a row (i.e.
                // ReEngagementEmailSentAt was still NULL) proceeds to send. This is what
                // makes the one-email guarantee hold across concurrent runs — see the
                // class remarks for what happens if the send below then fails.
                var claimed = await orgDb.Organizations
                    .Where(o => o.OrgId == org.OrgId && o.ReEngagementEmailSentAt == null)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(o => o.ReEngagementEmailSentAt, DateTimeOffset.UtcNow),
                        cancellationToken);

                if (claimed == 0) continue; // another instance/tick already claimed this org

                var token = tokenService.GenerateToken(org.OrgId);
                var unsubscribeUrl = $"{apiBaseUrl.TrimEnd('/')}/api/orgs/marketing/optout?orgId={Uri.EscapeDataString(org.OrgId)}&token={Uri.EscapeDataString(token)}";

                await emailService.SendReEngagementEmailAsync(
                    org.AdminContactEmail!, org.DisplayName, unsubscribeUrl, cancellationToken);

                sent++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send re-engagement email for OrgId={OrgId}",
                    LogSanitizer.SanitiseForLog(org.OrgId));
            }
        }

        logger.LogInformation("Never-used-org re-engagement campaign complete — {Sent}/{Candidates} emails sent.",
            sent, candidates.Count);

        return sent;
    }
}

/// <summary>
/// Periodically runs the never-used-org campaign. A no-op loop when
/// Marketing:ReEngagementEnabled is unset — <see cref="OrgEngagementService"/> checks the
/// flag itself on every tick, so this hosted service is always registered but only ever
/// sends email once an operator explicitly opts in.
/// </summary>
public class OrgEngagementBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<OrgEngagementBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = configuration.GetValue("Marketing:ReEngagementIntervalHours", 24);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(1, intervalHours)));

        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var engagementService = scope.ServiceProvider.GetRequiredService<IOrgEngagementService>();
                await engagementService.RunNeverUsedOrgCampaignAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Never-used-org re-engagement campaign tick failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
