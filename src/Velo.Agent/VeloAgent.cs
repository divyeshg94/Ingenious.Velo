using Velo.Agent.Tools;

namespace Velo.Agent;

/// <summary>
/// Foundry AI agent orchestration entry point.
/// Wraps the Microsoft Foundry Agent Framework and exposes a simplified chat interface
/// for use by Velo.Api's AgentController.
/// </summary>
public class VeloAgent(AgentConfig config, PipelineAnalysisTool pipelineTool, CodeAnalysisTool codeTool, RecommendationTool recommendationTool)
{
    public async Task<AgentResponse> ChatAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        // TODO: initialize Foundry agent client using config.FoundryEndpoint + Managed Identity
        // TODO: register tools (pipelineTool, codeTool, recommendationTool)
        // TODO: send message with history, stream response
        // TODO: cache response by pipeline fingerprint hash
        throw new NotImplementedException();
    }
}

public record AgentRequest(string OrgId, string ProjectId, string Message, IEnumerable<AgentMessage> History);
public record AgentMessage(string Role, string Content);
public record AgentResponse(string Content, IEnumerable<string> Citations, int TokensUsed);
