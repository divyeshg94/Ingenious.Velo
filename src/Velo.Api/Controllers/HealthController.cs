using Microsoft.AspNetCore.Mvc;
using Velo.Api.Services;
using Velo.Shared.Models;

namespace Velo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController(IDoraService doraService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TeamHealth>> GetTeamHealth(
        [FromQuery] string projectId,
        CancellationToken cancellationToken = default)
    {
        var health = await doraService.GetTeamHealthAsync(projectId, cancellationToken);
        return Ok(health);
    }
}
