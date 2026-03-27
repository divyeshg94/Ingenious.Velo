using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using Velo.Agent.Tools;

namespace Velo.Agent;

/// <summary>
/// Foundry AI agent orchestration entry point.
/// Uses Azure.AI.Agents.Persistent (GA) + AIProjectClient.GetPersistentAgentsClient().
///
/// Authentication (first match wins):
///   1. API key present                    → ApiKeyTokenCredential (simplest)
///   2. TenantId + ClientId + ClientSecret → ClientSecretCredential (cross-tenant SP)
///   3. None                               → DefaultAzureCredential (Velo Managed Identity)
///
/// Agent ID:
///   • Provided in config → used directly
///   • Null/empty         → agent is auto-created with Velo's default system prompt on first call;
///                          the returned ID is persisted via IAgentDataProvider.SaveAgentIdAsync
///                          so subsequent calls reuse it.
///
/// Architecture:
///   1. Tools gather DB context via IAgentDataProvider
///   2. Context is prepended to the user message as a structured block
///   3. A stateless thread is created per request (history replayed each time)
///   4. The agent runs, we poll until terminal state, then clean up the thread
/// </summary>
public class VeloAgent(
    AgentConfig config,
    IAgentDataProvider dataProvider,
    PipelineAnalysisTool pipelineTool,
    CodeAnalysisTool codeTool,
    RecommendationTool recommendationTool)
{
    private const string SystemPrompt = """
        You are Velo, an AI engineering intelligence assistant embedded in Azure DevOps.
        You help DevOps engineers improve their pipelines and engineering practices using
        real data from their ADO environment.

        Use any pipeline, DORA, and PR data provided in [VELO_CONTEXT] blocks to give
        specific, actionable recommendations grounded in the actual numbers.
        When data is missing, acknowledge it and suggest how to populate it (e.g. run a sync).
        """;

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
            ## Pipeline Build History
            {pipelineContext}

            ## DORA Metrics
            {doraContext}

            ## Pull Request Insights
            {prContext}
            """;

        // 2. Build the agents client — API key takes precedence over Managed Identity
        PersistentAgentsClient agentsClient = ResolveAgentsClient(config);

        // 3. Resolve (or auto-create) the Foundry agent
        var agentId = await ResolveAgentIdAsync(agentsClient, request.OrgId, cancellationToken);

        // 4. Create a new thread for this conversation (stateless — history replayed per request)
        PersistentAgentThread thread = await agentsClient.Threads.CreateThreadAsync(cancellationToken: cancellationToken);

        try
        {
            // 5. Replay conversation history so the agent has full context
            foreach (var msg in request.History)
            {
                var role = string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    ? MessageRole.Agent
                    : MessageRole.User;
                await agentsClient.Messages.CreateMessageAsync(
                    thread.Id, role, msg.Content, cancellationToken: cancellationToken);
            }

            // 6. Add the current user message with the Velo context block prepended
            var contextualMessage = $"[VELO_CONTEXT]\n{systemContext}\n[/VELO_CONTEXT]\n\n{request.Message}";
            await agentsClient.Messages.CreateMessageAsync(
                thread.Id, MessageRole.User, contextualMessage, cancellationToken: cancellationToken);

            // 7. Create and poll the agent run until it reaches a terminal state
            ThreadRun run = await agentsClient.Runs.CreateRunAsync(
                thread.Id, agentId, cancellationToken: cancellationToken);

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

            // 8. Extract the last assistant message (newest first)
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

    /// <summary>
    /// Returns the agent ID from config if present, otherwise auto-creates a new Foundry
    /// agent with Velo's default system prompt and persists the ID for future calls.
    /// </summary>
    private async Task<string> ResolveAgentIdAsync(
        PersistentAgentsClient agentsClient,
        string orgId,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(config.AgentId))
            return config.AgentId;

        // Auto-create the agent using the configured deployment model
        var created = await agentsClient.Administration.CreateAgentAsync(
            model: config.DeploymentName,
            name: "Velo Engineering Assistant",
            instructions: SystemPrompt,
            cancellationToken: ct);

        var newAgentId = created.Value.Id;

        // Persist so subsequent calls reuse the same agent (no per-call re-creation)
        await dataProvider.SaveAgentIdAsync(orgId, newAgentId, ct);

        // Update in-memory config so the rest of this request uses the new ID
        config.AgentId = newAgentId;

        return newAgentId;
    }

    /// <summary>
    /// Resolves the Foundry PersistentAgentsClient using the first matching credential:
    ///   1. API key            → ApiKeyTokenCredential (single-field, simplest)
    ///   2. Service principal  → ClientSecretCredential (cross-tenant SP)
    ///   3. Fallback           → DefaultAzureCredential via AIProjectClient (Velo Managed Identity)
    /// </summary>
    private static PersistentAgentsClient ResolveAgentsClient(AgentConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
            return new PersistentAgentsClient(
                config.FoundryEndpoint,
                new ApiKeyTokenCredential(config.ApiKey));

        if (!string.IsNullOrEmpty(config.TenantId)
            && !string.IsNullOrEmpty(config.ClientId)
            && !string.IsNullOrEmpty(config.ClientSecret))
            return new PersistentAgentsClient(
                config.FoundryEndpoint,
                new ClientSecretCredential(config.TenantId, config.ClientId, config.ClientSecret));

        var projectClient = new AIProjectClient(new Uri(config.FoundryEndpoint), new DefaultAzureCredential());
        return projectClient.GetPersistentAgentsClient();
    }

    /// <summary>
    /// Wraps an Azure AI Foundry API key as a <see cref="TokenCredential"/>.
    /// Azure AI Services accepts the resource key as a Bearer token value in addition
    /// to the standard <c>api-key</c> header, so this allows the SDK's built-in
    /// HTTP pipeline to carry the key without modification.
    /// </summary>
    private sealed class ApiKeyTokenCredential(string apiKey) : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(apiKey, DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(new AccessToken(apiKey, DateTimeOffset.MaxValue));
    }
}

public record AgentRequest(string OrgId, string ProjectId, string Message, IEnumerable<AgentMessage> History);
public record AgentMessage(string Role, string Content);
public record AgentResponse(string Content, IEnumerable<string> Citations, int TokensUsed);
