using System.Text.Json;

namespace Velo.Api.Services;

/// <summary>
/// Parses an Azure DevOps build timeline JSON response and produces a
/// human-readable concatenation of stage names ordered by their
/// <c>order</c> field.
///
/// The Timeline REST API returns a flat <c>records[]</c> array. Each record
/// has a <c>type</c> (Stage, Phase, Job, Task, Checkpoint, ...). We keep only
/// records whose type is "Stage" and join their names with " → ".
///
/// Output is truncated to 200 characters to fit the PipelineRun.StageName
/// column. Returns null when the timeline contains no stages.
/// </summary>
public static class AdoTimelineParser
{
    private const int MaxLength = 200;
    private const string Separator = " → ";

    public static string? ExtractStageNames(string? timelineJson)
    {
        if (string.IsNullOrWhiteSpace(timelineJson)) return null;

        List<(int Order, string Name)> stages = [];
        try
        {
            using var doc = JsonDocument.Parse(timelineJson);
            if (!doc.RootElement.TryGetProperty("records", out var records) ||
                records.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var record in records.EnumerateArray())
            {
                if (!record.TryGetProperty("type", out var typeEl) ||
                    typeEl.ValueKind != JsonValueKind.String) continue;
                if (!string.Equals(typeEl.GetString(), "Stage", StringComparison.OrdinalIgnoreCase)) continue;

                var name = record.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                    ? nameEl.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(name)) continue;

                var order = record.TryGetProperty("order", out var orderEl) && orderEl.ValueKind == JsonValueKind.Number
                    ? orderEl.GetInt32()
                    : int.MaxValue;

                stages.Add((order, name));
            }
        }
        catch (JsonException)
        {
            return null;
        }

        if (stages.Count == 0) return null;

        var joined = string.Join(Separator, stages
            .OrderBy(s => s.Order)
            .Select(s => s.Name));

        return joined.Length <= MaxLength ? joined : joined[..MaxLength];
    }
}
