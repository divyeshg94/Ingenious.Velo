using Microsoft.AspNetCore.Mvc;
using Velo.Api.Interface;
using Velo.Shared.Models;

namespace Velo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PipelinesController(IPipelineService pipelineService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PipelineRunDto>>> GetPipelines(
        [FromQuery] string projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var runs = await pipelineService.GetRunsAsync(projectId, page, pageSize, cancellationToken);
        return Ok(runs);
    }
}
