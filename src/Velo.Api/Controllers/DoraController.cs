using Microsoft.AspNetCore.Mvc;
using Velo.Api.Services;
using Velo.Shared.Models;

namespace Velo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DoraController(IDoraService doraService) : ControllerBase
{
    [HttpGet("metrics")]
    public async Task<ActionResult<DoraMetrics>> GetMetrics(
        [FromQuery] string projectId,
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var metrics = await doraService.GetMetricsAsync(projectId, days, cancellationToken);
        return Ok(metrics);
    }

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<DoraMetrics>>> GetHistory(
        [FromQuery] string projectId,
        [FromQuery] int days = 90,
        CancellationToken cancellationToken = default)
    {
        var history = await doraService.GetHistoryAsync(projectId, days, cancellationToken);
        return Ok(history);
    }
}
