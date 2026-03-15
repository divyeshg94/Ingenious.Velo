using Microsoft.AspNetCore.Mvc;
using Velo.Api.Services;

namespace Velo.Api.Controllers;

public record AgentChatRequest(string ProjectId, string Message, IEnumerable<ChatMessage> History);
public record ChatMessage(string Role, string Content);
public record AgentChatResponse(ChatMessage Message, IEnumerable<string> Citations);

[ApiController]
[Route("api/[controller]")]
public class AgentController(IAgentService agentService) : ControllerBase
{
    [HttpPost("chat")]
    public async Task<ActionResult<AgentChatResponse>> Chat(
        [FromBody] AgentChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await agentService.ChatAsync(request.ProjectId, request.Message, request.History, cancellationToken);
        return Ok(response);
    }
}
