using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Shared.Models.Requests;
using Velo.Api.Interface;

namespace Velo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ConnectionsController(IConnectionService connectionService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult> RegisterConnection(
        [FromBody] ConnectionConfig config,
        CancellationToken cancellationToken = default)
    {
        await connectionService.RegisterAsync(config.OrgUrl, config.PersonalAccessToken, cancellationToken);
        return NoContent();
    }

    [HttpDelete]
    public async Task<ActionResult> RemoveConnection(CancellationToken cancellationToken = default)
    {
        await connectionService.RemoveAsync(cancellationToken);
        return NoContent();
    }
}
