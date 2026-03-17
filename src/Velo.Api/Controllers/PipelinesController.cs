using Microsoft.AspNetCore.Mvc;
using Velo.Api.Services;
using Velo.Shared.Models;
using Velo.SQL.Models;

namespace Velo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PipelinesController(IPipelineService pipelineService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PipelineRun>>> GetPipelines(
        [FromQuery] string projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var runs = await pipelineService.GetRunsAsync(projectId, page, pageSize, cancellationToken);
        return Ok(runs);
    }

    [HttpGet("{pipelineId}/analysis")]
    public async Task<ActionResult> GetPipelineAnalysis(
        int pipelineId,
        CancellationToken cancellationToken = default)
    {
        var analysis = await pipelineService.GetAnalysisAsync(pipelineId, cancellationToken);
        return Ok(analysis);
    }
}
