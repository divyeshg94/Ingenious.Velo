namespace Velo.Shared.Models;

/// <summary>
/// Envelope wrapper for DORA metrics endpoints. Endpoints that may legitimately
/// return an "empty" state (e.g. a team filter with no mapped pipelines) use
/// this envelope so the response shape is stable across all paths and clients
/// can be generated from the OpenAPI schema without conditional handling.
/// </summary>
public class DoraMetricsResponse
{
    /// <summary>
    /// "ok" when <see cref="Metrics"/> is populated. "empty" when the filter
    /// resolved to no data (with <see cref="Note"/> explaining why).
    /// </summary>
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public string? Note { get; set; }
    public string? OrgId { get; set; }
    public string? ProjectId { get; set; }
    public string? RepositoryName { get; set; }
    public string? TeamName { get; set; }
    public DoraMetricsDto? Metrics { get; set; }
}

/// <summary>
/// History-shaped variant of <see cref="DoraMetricsResponse"/>. Always returns
/// a (possibly empty) history collection plus the same status/note envelope.
/// </summary>
public class DoraMetricsHistoryResponse
{
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public string? Note { get; set; }
    public string? OrgId { get; set; }
    public string? ProjectId { get; set; }
    public string? RepositoryName { get; set; }
    public string? TeamName { get; set; }
    public int? Days { get; set; }
    public IEnumerable<DoraMetricsDto> History { get; set; } = [];
}

/// <summary>
/// Envelope wrapper for the Team Health endpoint with the same status/note
/// pattern as <see cref="DoraMetricsResponse"/>.
/// </summary>
public class TeamHealthResponse
{
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public string? Note { get; set; }
    public string? OrgId { get; set; }
    public string? ProjectId { get; set; }
    public string? RepositoryName { get; set; }
    public string? TeamName { get; set; }
    public TeamHealthDto? Health { get; set; }
}
