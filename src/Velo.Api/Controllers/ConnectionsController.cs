using Microsoft.AspNetCore.Mvc;
using Velo.Api.Services;

namespace Velo.Api.Controllers;

public record ConnectionConfig(string OrgUrl, string PersonalAccessToken);

[ApiController]
[Route("api/[controller]")]
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
