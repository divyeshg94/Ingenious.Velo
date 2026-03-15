using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Velo.SQL.Models;

[Table("PipelineRuns")]
public class PipelineRun : AuditableEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string OrgId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ProjectId { get; set; } = string.Empty;

    public int AdoPipelineId { get; set; }

    [MaxLength(200)]
    public string PipelineName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string RunNumber { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Result { get; set; } = string.Empty; // succeeded, failed, canceled, partiallySucceeded

    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset? FinishTime { get; set; }
    public long? DurationMs { get; set; }

    public bool IsDeployment { get; set; }

    [MaxLength(200)]
    public string? StageName { get; set; }

    [MaxLength(200)]
    public string? TriggeredBy { get; set; }

    public DateTimeOffset IngestedAt { get; set; } = DateTimeOffset.UtcNow;
}
