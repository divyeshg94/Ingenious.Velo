using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Identity;
using Velo.Agent.Tools;

namespace Velo.Agent;

/// <summary>
/// Foundry AI agent orchestration entry point.
/// Uses Azure.AI.Agents.Persistent (GA) + AIProjectClient.GetPersistentAgentsClient().
///
/// Authentication:
///   • Service principal credentials present → ClientSecretCredential (cross-tenant, customer's own Foundry)
///   • No credentials                         → DefaultAzureCredential (Velo Managed Identity)
///
/// Architecture:
///   1. Tools gather DB context via IAgentDataProvider
///   2. Context is prepended to the user message as a structured block
///   3. A stateless thread is created per request (history replayed each time)
///   4. The agent runs, we poll until terminal state, then clean up the thread
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
            If data is missing or says "No data available", acknowledge it and suggest how to populate it.
            """;

        // 2. Resolve credential and build the agents client.
        //    PersistentAgentsClient only accepts TokenCredential (GA SDK constraint).
        //    • Service principal configured → ClientSecretCredential (customer's own Foundry resource, cross-tenant)
        //    • No service principal         → DefaultAzureCredential (Velo Managed Identity must have Foundry access)
        var credential = ResolveCredential(config);
        var projectClient = new AIProjectClient(new Uri(config.FoundryEndpoint), credential);
        PersistentAgentsClient agentsClient = projectClient.GetPersistentAgentsClient();

        // 3. Create a new thread for this conversation (stateless — history replayed per request)
        PersistentAgentThread thread = await agentsClient.Threads.CreateThreadAsync(cancellationToken: cancellationToken);

        try
        {
            // 4. Replay conversation history so the agent has full context
            foreach (var msg in request.History)
            {
                var role = string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    ? MessageRole.Agent
                    : MessageRole.User;
                await agentsClient.Messages.CreateMessageAsync(
                    thread.Id, role, msg.Content, cancellationToken: cancellationToken);
            }

            // 5. Add the current user message with the Velo context block prepended
            var contextualMessage = $"[VELO_CONTEXT]\n{systemContext}\n[/VELO_CONTEXT]\n\n{request.Message}";
            await agentsClient.Messages.CreateMessageAsync(
                thread.Id, MessageRole.User, contextualMessage, cancellationToken: cancellationToken);

            // 6. Create and poll the agent run until it reaches a terminal state
            ThreadRun run = await agentsClient.Runs.CreateRunAsync(
                thread.Id, config.AgentId, cancellationToken: cancellationToken);

            while (run.Status == RunStatus.Queued
                || run.Status == RunStatus.InProgress
                || run.Status == RunStatus.Cancelling)
            {
                await Task.Delay(1200, cancellationToken);
                run = await agentsClient.Runs.GetRunAsync(thread.Id, run.Id, cancellationToken);
            }

            if (run.Status == RunStatus.Failed)
                throw new InvalidOperationException(
                    $"Foundry agent run failed: {run.LastError?.Message ?? "Unknown error"}");

            if (run.Status == RunStatus.Cancelled || run.Status == RunStatus.Expired)
                throw new InvalidOperationException(
                    $"Foundry agent run ended with status: {run.Status}");

            // 7. Extract the last assistant message (newest first)
            var messages = agentsClient.Messages.GetMessages(thread.Id, order: ListSortOrder.Descending);
            var lastAssistant = messages.FirstOrDefault(m => m.Role == MessageRole.Agent);

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
            try { await agentsClient.Threads.DeleteThreadAsync(thread.Id, cancellationToken); }
            catch { /* intentionally swallowed — thread will expire naturally */ }
        }
    }

    private static Azure.Core.TokenCredential ResolveCredential(AgentConfig config)
    {
        // Service principal credentials present → use ClientSecretCredential (cross-tenant)
        if (!string.IsNullOrEmpty(config.TenantId)
            && !string.IsNullOrEmpty(config.ClientId)
            && !string.IsNullOrEmpty(config.ClientSecret))
        {
            return new ClientSecretCredential(config.TenantId, config.ClientId, config.ClientSecret);
        }

        // Fallback: Velo Managed Identity (customer must grant it Foundry access)
        return new DefaultAzureCredential();
    }
}

public record AgentRequest(string OrgId, string ProjectId, string Message, IEnumerable<AgentMessage> History);
public record AgentMessage(string Role, string Content);
public record AgentResponse(string Content, IEnumerable<string> Citations, int TokensUsed);
