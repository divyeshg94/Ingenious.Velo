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
}
