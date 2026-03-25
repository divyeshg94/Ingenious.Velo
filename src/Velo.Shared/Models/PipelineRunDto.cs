namespace Velo.Shared.Models;

public class PipelineRunDto
{
    public Guid Id { get; set; }
    public string OrgId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public int AdoPipelineId { get; set; }
    public string PipelineName { get; set; } = string.Empty;
    public string RunNumber { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset? FinishTime { get; set; }
    public long? DurationMs { get; set; }
    public bool IsDeployment { get; set; }
    public string? StageName { get; set; }
    public string? TriggeredBy { get; set; }
    public string? RepositoryName { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
}
