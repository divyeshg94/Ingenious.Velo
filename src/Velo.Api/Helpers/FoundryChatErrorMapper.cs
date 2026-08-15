using Azure;

namespace Velo.Api.Helpers;

/// <summary>
/// Maps a <see cref="RequestFailedException"/> raised by the Foundry Agents/Responses call in
/// <see cref="Velo.Agent.VeloAgent.ChatAsync"/> to a user-actionable <see cref="InvalidOperationException"/>.
/// Pulled out of <see cref="Velo.Api.Interface.AgentService"/> so the status-code branching
/// (404/429/401/403) can be unit tested without exercising the real Foundry SDK call chain.
/// </summary>
public static class FoundryChatErrorMapper
{
    /// <summary>Returns null when the status code isn't one this mapper handles — caller should let it propagate.</summary>
    public static InvalidOperationException? Map(RequestFailedException ex, string foundryEndpoint, string deploymentName)
    {
        switch (ex.Status)
        {
            case 404:
                return Map404(ex, foundryEndpoint, deploymentName);

            case 429:
                return new InvalidOperationException(
                    "Agent rate limit exceeded (429 Too Many Requests). " +
                    "Your Azure AI Foundry resource has reached its request quota. " +
                    "Please wait a moment and try again.",
                    ex);

            case 401 or 403:
                // The Foundry Agents/Responses management surface is Entra ID (AAD) only — API keys
                // are never attempted here regardless of what's stored on AgentConfig (see
                // FoundryClientFactory). Foundry returns 401 (not 403) for RBAC "PermissionDenied"
                // data-action failures — e.g. the identity has 'Azure AI User' but the role assignment
                // hasn't propagated, or is scoped to the wrong resource. Either status means the
                // configured identity lacks the access this call needs.
                // ex is kept as the inner exception (not surfaced to the client) so the raw Foundry
                // message — which can include internal identifiers/request details — stays in server
                // logs only, per AgentController's logger.LogWarning(ex, ...) on this path.
                return new InvalidOperationException(
                    $"Agent authentication failed ({ex.Status} {(ex.Status == 401 ? "Unauthorized" : "Forbidden")}). " +
                    "Verify that the configured identity (Service Principal, or Velo's Managed Identity if " +
                    "none is configured) has the 'Azure AI User' role assigned on the Foundry resource, and that " +
                    "the role assignment has finished propagating (can take several minutes after granting it). " +
                    "API keys are not supported for agent calls — see docs/foundry-agent-setup.md.",
                    ex);

            default:
                return null;
        }
    }

    private static InvalidOperationException Map404(RequestFailedException ex, string foundryEndpoint, string deploymentName)
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
