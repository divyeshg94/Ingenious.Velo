using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Velo.SQL.Models;

/// <summary>
/// Organization-level settings (one row per org).
/// Currently stores the email address to send feedback notifications to.
/// </summary>
[Table("OrganizationSettings")]
public class OrganizationSettings
{
    [Required, MaxLength(100)]
    public string OrgId { get; set; } = string.Empty;

    /// <summary>
    /// Email address to send feedback notifications to.
    /// If null or empty, no notifications are sent.
    /// </summary>
    [MaxLength(500)]
    public string? FeedbackNotificationEmail { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
