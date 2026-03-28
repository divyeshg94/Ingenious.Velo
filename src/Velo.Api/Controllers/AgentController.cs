using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Api.Interface;

namespace Velo.Api.Controllers;

public record AgentChatRequest(string ProjectId, string Message, IEnumerable<ChatMessage> History);
public record ChatMessage(string Role, string Content);
public record AgentChatResponse(ChatMessage Message, IEnumerable<string> Citations);

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AgentController(
    IAgentService agentService,
    ILogger<AgentController> logger) : ControllerBase
{
    [HttpPost("chat")]
    public async Task<ActionResult<AgentChatResponse>> Chat(
        [FromBody] AgentChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        if (string.IsNullOrEmpty(orgId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.ProjectId))
            return BadRequest(new { error = "projectId is required" });

        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "message is required" });

        try
        {
            var response = await agentService.ChatAsync(
                orgId, request.ProjectId, request.Message, request.History, cancellationToken);

            return Ok(response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
        {
            return BadRequest(new { error = ex.Message, code = "AGENT_NOT_CONFIGURED" });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("authentication failed"))
        {
            // Surfaced from AgentService when Foundry returns 403 — return 400 with the full message
            // so the Angular UI can display it directly rather than a generic "try again".
            logger.LogWarning(ex,
                "AGENT: Auth failed — OrgId={OrgId}, ProjectId={ProjectId}",
                orgId, request.ProjectId);

            return BadRequest(new { error = ex.Message, code = "AGENT_AUTH_FAILED" });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found (404)"))
        {
            // Surfaced from AgentService when Foundry returns 404 — wrong endpoint type or
            // model deployment name doesn't exist.
            logger.LogWarning(ex,
                "AGENT: Resource not found — OrgId={OrgId}, ProjectId={ProjectId}",
                orgId, request.ProjectId);

            return BadRequest(new { error = ex.Message, code = "AGENT_RESOURCE_NOT_FOUND" });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("rate limit exceeded"))
        {
            // Surfaced from AgentService when Foundry returns 429 — pass the 429 status through
            // so the Angular client knows it can retry rather than treating it as a hard failure.
            logger.LogWarning(ex,
                "AGENT: Rate limited — OrgId={OrgId}, ProjectId={ProjectId}",
                orgId, request.ProjectId);

            return StatusCode(429, new { error = ex.Message, code = "AGENT_RATE_LIMITED" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "AGENT: Chat failed — OrgId={OrgId}, ProjectId={ProjectId}",
                orgId, request.ProjectId);

            return StatusCode(500, new { error = "Agent request failed. Please try again." });
        }
    }
}
