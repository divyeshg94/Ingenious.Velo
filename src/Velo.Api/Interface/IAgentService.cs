using Azure;
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

        var (apiKey, tenantId, clientId, clientSecret) =
            await configService.GetDecryptedCredentialsAsync(orgId, cancellationToken);

        var agentConfig = new AgentConfig
        {
            OrgId = orgId,
            FoundryEndpoint = config.FoundryEndpoint,
            AgentId = string.IsNullOrWhiteSpace(config.AgentId) ? null : config.AgentId,
            DeploymentName = string.IsNullOrWhiteSpace(config.DeploymentName) ? "gpt-4o" : config.DeploymentName,
            ApiKey = apiKey,
            TenantId = tenantId,
            ClientId = clientId,
            ClientSecret = clientSecret,
        };

        var pipelineTool = new PipelineAnalysisTool(dataProvider);
        var codeTool = new CodeAnalysisTool(dataProvider);
        var recommendationTool = new RecommendationTool(dataProvider);
        var agent = new VeloAgent(agentConfig, pipelineTool, codeTool, recommendationTool);

        var agentHistory = history.Select(m => new AgentMessage(m.Role, m.Content));
        var request = new AgentRequest(orgId, projectId, message, agentHistory);

        logger.LogInformation(
            "AGENT: Chat request — OrgId={OrgId}, ProjectId={ProjectId}", orgId, projectId);

        AgentResponse response;
        try
        {
            response = await agent.ChatAsync(request, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // 404 here usually means one of:
            //   a) The model deployment name does not exist in the Foundry project.
            //   b) An Azure OpenAI endpoint (*.openai.azure.com) was supplied — not a Foundry
            //      project endpoint.
            //   c) The Foundry project endpoint is missing the /api/projects/<project> path
            //      segment (a bare hub endpoint 404s on project-scoped calls).
            var isOpenAIEndpoint = agentConfig.FoundryEndpoint.Contains(
                ".openai.azure.com", StringComparison.OrdinalIgnoreCase);

            if (isOpenAIEndpoint)
                throw new InvalidOperationException(
                    "Resource not found (404). " +
                    "Azure OpenAI endpoints (*.openai.azure.com) are not Foundry project endpoints. " +
                    "Use your project endpoint instead: Microsoft Foundry portal → your project → Overview → " +
                    "'Project endpoint' (format: https://<hub>.services.ai.azure.com/api/projects/<project>). " +
                    "See docs/foundry-agent-setup.md for details.");

            throw new InvalidOperationException(
                "Resource not found (404). Check the following: " +
                "(1) The endpoint includes the /api/projects/<project> path — a bare hub endpoint 404s. " +
                "(2) The Model Deployment Name (e.g. 'gpt-4o') exactly matches a deployment that exists in your Foundry project. " +
                $"Current deployment name: '{agentConfig.DeploymentName}'. " +
                "Verify it in the Foundry portal → your project → Deployments. See docs/foundry-agent-setup.md.");
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            throw new InvalidOperationException(
                "Agent rate limit exceeded (429 Too Many Requests). " +
                "Your Azure AI Foundry resource has reached its request quota. " +
                "Please wait a moment and try again.");
        }
        catch (RequestFailedException ex) when (ex.Status is 401 or 403)
        {
            // The Foundry Agents/Responses management surface is Entra ID (AAD) only — API keys
            // are never attempted here regardless of what's stored on AgentConfig (see
            // FoundryClientFactory). Foundry returns 401 (not 403) for RBAC "PermissionDenied"
            // data-action failures — e.g. the identity has 'Azure AI User' but the role assignment
            // hasn't propagated, or is scoped to the wrong resource. Either status means the
            // configured identity lacks the access this call needs.
            throw new InvalidOperationException(
                $"Agent authentication failed ({ex.Status} {(ex.Status == 401 ? "Unauthorized" : "Forbidden")}). " +
                "Verify that the configured identity (Service Principal, or Velo's Managed Identity if " +
                "none is configured) has the 'Azure AI User' role assigned on the Foundry resource, and that " +
                "the role assignment has finished propagating (can take several minutes after granting it). " +
                "API keys are not supported for agent calls — see docs/foundry-agent-setup.md. " +
                $"Details: {ex.Message}");
        }

        logger.LogInformation(
            "AGENT: Chat response — OrgId={OrgId}, TokensUsed={Tokens}", orgId, response.TokensUsed);

        return new AgentChatResponse(
            new ChatMessage("assistant", response.Content),
            response.Citations);
    }
}
