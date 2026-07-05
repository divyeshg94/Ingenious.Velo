using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Velo.SQL.Models;

/// <summary>
/// Tracks application users and their access patterns.
/// One row per unique (org, user_email) pair.
/// Records first access time and last access time for analytics.
/// </summary>
[Table("ApplicationUsers")]
public class ApplicationUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Maps to the row-level org scope (e.g. "my-ado-org").</summary>
    [Required, MaxLength(100)]
    public string OrgId { get; set; } = string.Empty;

    /// <summary>User's email address from Azure AD B2C token (oid claim -> email resolution).</summary>
    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    /// <summary>User's display name from Azure AD token (if available).</summary>
    [MaxLength(200)]
    public string? DisplayName { get; set; }

    /// <summary>First time this user accessed the application.</summary>
    public DateTimeOffset FirstAccessAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Most recent time this user accessed the application.</summary>
    public DateTimeOffset LastAccessAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Total number of times this user has accessed the application.</summary>
    public int AccessCount { get; set; } = 1;
}
