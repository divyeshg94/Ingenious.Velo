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
        var agent = new VeloAgent(agentConfig, dataProvider, pipelineTool, codeTool, recommendationTool);

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
            // 404 on CreateAgentAsync / CreateThreadAsync usually means one of:
            //   a) The model deployment name does not exist in the Foundry project.
            //   b) An Azure OpenAI endpoint (*.openai.azure.com) was supplied — those endpoints
            //      serve the OpenAI Assistants API, NOT the Azure AI Agents Persistent API.
            //      The correct endpoint is the Azure AI Foundry project endpoint.
            //   c) The Foundry project endpoint URL is malformed or points to the wrong resource.
            var isOpenAIEndpoint = agentConfig.FoundryEndpoint.Contains(
                ".openai.azure.com", StringComparison.OrdinalIgnoreCase);

            if (isOpenAIEndpoint)
                throw new InvalidOperationException(
                    "Resource not found (404). " +
                    "Azure OpenAI endpoints (*.openai.azure.com) are not compatible with the Azure AI Agents API. " +
                    "Please use your Azure AI Foundry project endpoint instead: " +
                    "Azure AI Studio → your project → Overview → Project endpoint " +
                    "(format: https://<hub>.services.ai.azure.com or https://<project>.api.azureml.ms).");

            throw new InvalidOperationException(
                "Resource not found (404). Check the following: " +
                "(1) The endpoint is your Azure AI Foundry project endpoint — not an Azure OpenAI endpoint. " +
                "(2) The Model Deployment Name (e.g. 'gpt-4o') exactly matches a deployment that exists in your Foundry project. " +
                $"Current deployment name: '{agentConfig.DeploymentName}'. " +
                "Verify it in Azure AI Studio → your project → Deployments.");
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            throw new InvalidOperationException(
                "Agent rate limit exceeded (429 Too Many Requests). " +
                "Your Azure AI Foundry resource has reached its request quota. " +
                "Please wait a moment and try again.");
        }
        catch (RequestFailedException ex) when (ex.Status == 403)
        {
            // AzureML workspace endpoints (*.api.azureml.ms) do NOT support the api-key header —
            // only Azure AI Services endpoints (*.services.ai.azure.com) do. When an API key is
            // used against an AzureML endpoint the request arrives with an empty identity and
            // Foundry returns 403 "Identity(object id: ) does not have permissions".
            var isApiKeyAuth = !string.IsNullOrEmpty(agentConfig.ApiKey);
            var isAzureMLEndpoint = agentConfig.FoundryEndpoint.Contains(
                ".api.azureml.ms", StringComparison.OrdinalIgnoreCase);

            if (isApiKeyAuth && isAzureMLEndpoint)
                throw new InvalidOperationException(
                    "Agent authentication failed (403 Forbidden). " +
                    "AzureML workspace endpoints (*.api.azureml.ms) do not support API key authentication. " +
                    "Please switch to the Service Principal tab and provide Tenant ID, Client ID, and Client Secret, " +
                    "or use Velo's Managed Identity instead. " +
                    "API keys are only supported for Azure AI Services endpoints (*.services.ai.azure.com).");

            if (isApiKeyAuth)
                throw new InvalidOperationException(
                    "Agent authentication failed (403 Forbidden). " +
                    "Verify that the API key is correct and that the Foundry resource grants API key access.");

            throw new InvalidOperationException(
                "Agent authentication failed (403 Forbidden). " +
                "Verify that the Foundry resource grants the configured identity (Managed Identity or Service Principal) " +
                "the 'Azure AI User' role.");
        }

        logger.LogInformation(
            "AGENT: Chat response — OrgId={OrgId}, TokensUsed={Tokens}", orgId, response.TokensUsed);

        return new AgentChatResponse(
            new ChatMessage("assistant", response.Content),
            response.Citations);
    }
}
