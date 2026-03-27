using Azure.AI.Projects;
using Azure.Identity;
using Velo.Agent.Tools;

namespace Velo.Agent;

/// <summary>
/// Foundry AI agent orchestration entry point.
/// Wraps the Azure AI Projects AgentsClient and exposes a simplified chat interface
/// for use by Velo.Api's AgentController.
///
/// Architecture:
///   1. Tools gather DB context via IAgentDataProvider (injected from Velo.Api)
///   2. Context is prepended to the user message as a structured system block
///   3. A stateless thread is created per request (history replayed each time)
///   4. The Azure AI Foundry agent runs and responds; thread is cleaned up after
/// </summary>
public class VeloAgent(
    AgentConfig config,
    PipelineAnalysisTool pipelineTool,
    CodeAnalysisTool codeTool,
    RecommendationTool recommendationTool)
{
    public async Task<AgentResponse> ChatAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        // 1. Build rich context strings from DB via tools
        var pipelineContext = await pipelineTool.GetBuildHistoryAsync(
            request.OrgId, request.ProjectId, cancellationToken);

        var doraContext = await recommendationTool.GetDoraRecommendationsAsync(
            request.OrgId, request.ProjectId, cancellationToken);

        var prContext = await codeTool.GetPrSizeMetricsAsync(
            request.OrgId, request.ProjectId, cancellationToken);

        var systemContext = $"""
            You are Velo, an AI engineering intelligence assistant embedded in Azure DevOps.
            You help DevOps engineers improve their pipelines and engineering practices using real data from their ADO environment.

            ## Pipeline Build History
            {pipelineContext}

            ## DORA Metrics
            {doraContext}

            ## Pull Request Insights
            {prContext}

            Use the data above to give specific, actionable recommendations grounded in the actual numbers.
            When the user asks a general question, reference the most relevant data points from the context above.
            If data is missing or says "No data available", acknowledge it and suggest how to populate it (e.g. trigger a sync).
            """;

        // 2. Connect to Azure AI Foundry.
        //    • API key present  → customer-supplied AzureKeyCredential (cross-tenant, no RBAC setup needed)
        //    • No API key       → DefaultAzureCredential (Velo Managed Identity must have Foundry access)
        AgentsClient agentsClient;
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            agentsClient = new AgentsClient(new Uri(config.FoundryEndpoint), new AzureKeyCredential(config.ApiKey));
        }
        else
        {
            var projectClient = new AIProjectClient(new Uri(config.FoundryEndpoint), new DefaultAzureCredential());
            agentsClient = projectClient.GetAgentsClient();
        }

        // 3. Create a new thread for this conversation (stateless — history replayed per request)
        var thread = (await agentsClient.CreateThreadAsync(cancellationToken)).Value;

        try
        {
            // 4. Replay conversation history so the agent has full context
            foreach (var msg in request.History)
            {
                var role = string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    ? MessageRole.Agent
                    : MessageRole.User;
                await agentsClient.CreateMessageAsync(
                    thread.Id, role, msg.Content, cancellationToken: cancellationToken);
            }

            // 5. Add the current user message with the Velo context block prepended
            var contextualMessage = $"[VELO_CONTEXT]\n{systemContext}\n[/VELO_CONTEXT]\n\n{request.Message}";
            await agentsClient.CreateMessageAsync(
                thread.Id, MessageRole.User, contextualMessage, cancellationToken: cancellationToken);

            // 6. Create and poll the agent run until it reaches a terminal state
            var run = (await agentsClient.CreateRunAsync(
                thread.Id, config.AgentId, cancellationToken: cancellationToken)).Value;

            while (run.Status == RunStatus.Queued
                || run.Status == RunStatus.InProgress
                || run.Status == RunStatus.Cancelling)
            {
                await Task.Delay(1200, cancellationToken);
                run = (await agentsClient.GetRunAsync(thread.Id, run.Id, cancellationToken)).Value;
            }

            if (run.Status == RunStatus.Failed)
                throw new InvalidOperationException(
                    $"Foundry agent run failed: {run.LastError?.Message ?? "Unknown error"}");

            if (run.Status is RunStatus.Cancelled or RunStatus.Expired)
                throw new InvalidOperationException(
                    $"Foundry agent run ended with status: {run.Status}");

            // 7. Extract the last assistant message
            var messages = (await agentsClient.GetMessagesAsync(
                thread.Id, cancellationToken: cancellationToken)).Value;

            var lastAssistant = messages.Data
                .Where(m => m.Role == MessageRole.Agent)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefault();

            var content = lastAssistant?.ContentItems
                .OfType<MessageTextContent>()
                .FirstOrDefault()?.Text
                ?? "I was unable to generate a response. Please try again.";

            var tokensUsed = run.Usage?.TotalTokens ?? 0;

            return new AgentResponse(content, [], (int)tokensUsed);
        }
        finally
        {
            // Best-effort thread cleanup to avoid accumulation in the Foundry project
            try { await agentsClient.DeleteThreadAsync(thread.Id, cancellationToken); }
            catch { /* intentionally swallowed — thread will expire naturally */ }
        }
    }
}

public record AgentRequest(string OrgId, string ProjectId, string Message, IEnumerable<AgentMessage> History);
public record AgentMessage(string Role, string Content);
public record AgentResponse(string Content, IEnumerable<string> Citations, int TokensUsed);
