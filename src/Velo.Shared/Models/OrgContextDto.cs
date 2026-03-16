namespace Velo.Shared.Models;

public class OrgContextDto
{
    public string OrgId { get; set; } = string.Empty;
    public string OrgUrl { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsPremium { get; set; }
    public int DailyTokenBudget { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
}
