using Velo.Agent;
using Velo.Agent.Tools;
using Velo.Api.Controllers;

namespace Velo.Api.Interface;

public interface IAgentService
{
    Task<AgentChatResponse> ChatAsync(
        string orgId,
        string projectId,
        string message,
        IEnumerable<ChatMessage> history,
        CancellationToken cancellationToken);
}

public class AgentService(
    IAgentConfigService configService,
    IAgentDataProvider dataProvider,
    ILogger<AgentService> logger) : IAgentService
{
    public async Task<AgentChatResponse> ChatAsync(
        string orgId,
        string projectId,
        string message,
        IEnumerable<ChatMessage> history,
        CancellationToken cancellationToken)
    {
        var config = await configService.GetConfigAsync(orgId, cancellationToken);

        if (config is null || !config.IsEnabled)
            throw new InvalidOperationException(
                "Foundry agent is not configured for this organization. " +
                "Please connect an agent in the Agent tab first.");

        var decryptedKey = await configService.GetDecryptedApiKeyAsync(orgId, cancellationToken);

        var agentConfig = new AgentConfig
        {
            FoundryEndpoint = config.FoundryEndpoint,
            AgentId = config.AgentId,
            ApiKey = decryptedKey,
        };

        var pipelineTool = new PipelineAnalysisTool(dataProvider);
        var codeTool = new CodeAnalysisTool(dataProvider);
        var recommendationTool = new RecommendationTool(dataProvider);
        var agent = new VeloAgent(agentConfig, pipelineTool, codeTool, recommendationTool);

        var agentHistory = history.Select(m => new AgentMessage(m.Role, m.Content));
        var request = new AgentRequest(orgId, projectId, message, agentHistory);

        logger.LogInformation(
            "AGENT: Chat request — OrgId={OrgId}, ProjectId={ProjectId}", orgId, projectId);

        var response = await agent.ChatAsync(request, cancellationToken);

        logger.LogInformation(
            "AGENT: Chat response — OrgId={OrgId}, TokensUsed={Tokens}", orgId, response.TokensUsed);

        return new AgentChatResponse(
            new ChatMessage("assistant", response.Content),
            response.Citations);
    }
}
