using System.Text.Json.Serialization;

namespace Velo.Shared.Models;

public class OrgContextDto
{
    public string OrgId { get; set; } = string.Empty;
    public string OrgUrl { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    // SECURITY: IsPremium and DailyTokenBudget are server-controlled fields.
    // [JsonIgnore(Condition = WhenWritingDefault)] ensures they are readable in GET responses
    // but cannot be set by a client via POST/PUT body — the JSON deserializer will ignore them.
    // Controllers always hardcode these to safe defaults when creating orgs.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPremium { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int DailyTokenBudget { get; set; }

    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>Null until the first historical sync has been triggered for this org.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }
}
