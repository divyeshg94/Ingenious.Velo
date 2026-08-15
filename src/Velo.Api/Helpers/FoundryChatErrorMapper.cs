using System.ClientModel;

namespace Velo.Api.Helpers;

/// <summary>
/// Maps a <see cref="ClientResultException"/> raised by the Foundry Agents/Responses call in
/// <see cref="Velo.Agent.VeloAgent.ChatAsync"/> to a user-actionable <see cref="InvalidOperationException"/>.
///
/// IMPORTANT: Azure.AI.Projects 2.x (which Microsoft.Agents.AI.Foundry's AsAIAgent sits on top of)
/// throws System.ClientModel.ClientResultException, NOT Azure.RequestFailedException — despite the
/// two producing near-identical "Service request failed. Status: ###" messages, which is what made
/// this easy to get wrong (see 2026-08-15 incident: a status-code catch written against
/// RequestFailedException silently never matched, so every 401/403/404/429 fell through to a
/// generic handler with no actionable message and nothing logged).
///
/// Takes the status/message/exception as plain values (rather than a <see cref="ClientResultException"/>
/// directly) so tests can exercise the branching without constructing a real one — ClientResultException's
/// Status is derived from a PipelineResponse with no public settable constructor, so faking one in a
/// test is more trouble than it's worth for what is otherwise pure string logic.
/// </summary>
public static class FoundryChatErrorMapper
{
    /// <summary>Returns null when the status code isn't one this mapper handles — caller should let it propagate.</summary>
    public static InvalidOperationException? Map(ClientResultException ex, string foundryEndpoint, string deploymentName)
        => Map(ex.Status, ex.Message, ex, foundryEndpoint, deploymentName);

    /// <summary>Returns null when the status code isn't one this mapper handles — caller should let it propagate.</summary>
    public static InvalidOperationException? Map(
        int status, string rawMessage, Exception originalException, string foundryEndpoint, string deploymentName)
    {
        switch (status)
        {
            case 404:
                return Map404(originalException, foundryEndpoint, deploymentName);

            case 429:
                return new InvalidOperationException(
                    "Agent rate limit exceeded (429 Too Many Requests). " +
                    "Your Azure AI Foundry resource has reached its request quota. " +
                    "Please wait a moment and try again.",
                    originalException);

            case 401 or 403:
                // The Foundry Agents/Responses management surface is Entra ID (AAD) only — API keys
                // are never attempted here regardless of what's stored on AgentConfig (see
                // FoundryClientFactory). Foundry returns 401 (not 403) for RBAC "PermissionDenied"
                // data-action failures — e.g. the identity has 'Azure AI User' but the role assignment
                // hasn't propagated, or is scoped to the wrong resource. Either status means the
                // configured identity lacks the access this call needs.
                // originalException is kept as the inner exception (not surfaced to the client) so the
                // raw Foundry message — which can include internal identifiers/request details — stays
                // in server logs only, per AgentController's logger.LogWarning(ex, ...) on this path.
                return new InvalidOperationException(
                    $"Agent authentication failed ({status} {(status == 401 ? "Unauthorized" : "Forbidden")}). " +
                    "Verify that the configured identity (Service Principal, or Velo's Managed Identity if " +
                    "none is configured) has the 'Azure AI User' role assigned on the Foundry resource, and that " +
                    "the role assignment has finished propagating (can take several minutes after granting it). " +
                    "API keys are not supported for agent calls — see docs/foundry-agent-setup.md.",
                    originalException);

            default:
                return null;
        }
    }

    private static InvalidOperationException Map404(Exception ex, string foundryEndpoint, string deploymentName)
    {
        // 404 here usually means one of:
        //   a) The model deployment name does not exist in the Foundry project.
        //   b) An Azure OpenAI endpoint (*.openai.azure.com) was supplied — not a Foundry
        //      project endpoint.
        //   c) The Foundry project endpoint is missing the /api/projects/<project> path
        //      segment (a bare hub endpoint 404s on project-scoped calls).
        var isOpenAIEndpoint = foundryEndpoint.Contains(".openai.azure.com", StringComparison.OrdinalIgnoreCase);

        if (isOpenAIEndpoint)
            return new InvalidOperationException(
                "Resource not found (404). " +
                "Azure OpenAI endpoints (*.openai.azure.com) are not Foundry project endpoints. " +
                "Use your project endpoint instead: Microsoft Foundry portal → your project → Overview → " +
                "'Project endpoint' (format: https://<hub>.services.ai.azure.com/api/projects/<project>). " +
                "See docs/foundry-agent-setup.md for details.",
                ex);

        return new InvalidOperationException(
            "Resource not found (404). Check the following: " +
            "(1) The endpoint includes the /api/projects/<project> path — a bare hub endpoint 404s. " +
            "(2) The Model Deployment Name (e.g. 'gpt-4o') exactly matches a deployment that exists in your Foundry project. " +
            $"Current deployment name: '{deploymentName}'. " +
            "Verify it in the Foundry portal → your project → Deployments. See docs/foundry-agent-setup.md.",
            ex);
    }
}
