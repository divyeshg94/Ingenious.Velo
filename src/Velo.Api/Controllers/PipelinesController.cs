using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Api.Interface;
using Velo.Shared.Models;

namespace Velo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]   // SECURITY: was missing — pipeline run data is tenant-scoped PII
public class PipelinesController(IPipelineService pipelineService) : ControllerBase
{
    private const int MaxPageSize = 200;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PipelineRunDto>>> GetPipelines(
        [FromQuery] string projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return BadRequest(new { error = "projectId is required" });

        // Clamp page size to prevent unbounded result-set dumps
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page     = Math.Max(1, page);

        var runs = await pipelineService.GetRunsAsync(projectId, page, pageSize, cancellationToken);
        return Ok(runs);
    }
}
