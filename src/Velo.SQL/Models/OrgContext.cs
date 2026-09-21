using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Velo.SQL.Models;

[Table("Organizations")]
public class OrgContext : AuditableEntity
{
    [Key, MaxLength(100)]
    public string OrgId { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string OrgUrl { get; set; } = string.Empty;

    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    public bool IsPremium { get; set; }

    public int DailyTokenBudget { get; set; } = 50_000;

    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>
    /// Set when a background historical sync has been successfully kicked off or completed.
    /// Used to avoid re-triggering auto-sync on every request for the same org.
    /// </summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>
    /// SECURITY: The AAD tenant GUID extracted from the 'tid' claim of the first authenticated
    /// VSSO token for this org. Once set, all subsequent requests must carry a token with the
    /// same tid — this prevents X-Azure-DevOps-OrgId header spoofing from a different tenant.
    /// </summary>
    [MaxLength(100)]
    public string? AadTenantId { get; set; }

    /// <summary>
    /// Contact email captured when the org registers (POST /api/orgs/connect), used only
    /// for the never-used-org re-engagement campaign. Never populated by inference or
    /// scraped from tokens — only what the org explicitly provides.
    /// </summary>
    [MaxLength(320)]
    public string? AdminContactEmail { get; set; }

    /// <summary>
    /// Set when the admin contact clicks the unsubscribe link in a re-engagement email.
    /// Once true, the org is permanently excluded from marketing sends.
    /// </summary>
    public bool MarketingOptOut { get; set; }

    /// <summary>
    /// Set once the never-used-org re-engagement email has been sent, so the campaign
    /// never emails the same org twice.
    /// </summary>
    public DateTimeOffset? ReEngagementEmailSentAt { get; set; }

    /// <summary>First-value onboarding milestone: first time DORA metrics were successfully viewed.</summary>
    public DateTimeOffset? FirstDoraViewedAt { get; set; }

    /// <summary>First-value onboarding milestone: first time a dashboard share link was created.</summary>
    public DateTimeOffset? FirstShareCreatedAt { get; set; }

    // ModifiedDate is inherited from AuditableEntity — no re-declaration needed.
}
