namespace Velo.Functions.Models;

public class PipelineRunEvent
{
    public string OrgId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public int PipelineId { get; set; }
    public string PipelineName { get; set; } = string.Empty;
    public string RunNumber { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty; // succeeded, failed, canceled
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset FinishTime { get; set; }
    public bool IsDeployment { get; set; }
    public string? StageName { get; set; }
    public string? TriggeredBy { get; set; }
}
