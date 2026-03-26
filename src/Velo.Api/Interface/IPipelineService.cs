using Microsoft.EntityFrameworkCore;
using Velo.SQL;
using Velo.Shared.Models;

namespace Velo.Api.Interface;

public interface IPipelineService
{
    Task<IEnumerable<PipelineRunDto>> GetRunsAsync(string projectId, int page, int pageSize, CancellationToken cancellationToken);
}

public class PipelineService(VeloDbContext db) : IPipelineService
{
    public async Task<IEnumerable<PipelineRunDto>> GetRunsAsync(
        string projectId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var runs = await db.PipelineRuns
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.StartTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return runs.Select(r => new PipelineRunDto
        {
            Id = r.Id,
            OrgId = r.OrgId,
            ProjectId = r.ProjectId,
            AdoPipelineId = r.AdoPipelineId,
            PipelineName = r.PipelineName,
            RunNumber = r.RunNumber,
            Result = r.Result,
            StartTime = r.StartTime,
            FinishTime = r.FinishTime,
            DurationMs = r.DurationMs,
            IsDeployment = r.IsDeployment,
            StageName = r.StageName,
            TriggeredBy = r.TriggeredBy,
            IngestedAt = r.IngestedAt,
            RepositoryName = r.RepositoryName
        });
    }
}
