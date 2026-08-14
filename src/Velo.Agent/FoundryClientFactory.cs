using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using Microsoft.Agents.AI;

namespace Velo.Agent;

/// <summary>
/// Central factory for creating <see cref="AIAgent"/> instances against a Microsoft Foundry
/// project (Responses-based Agents service, via <c>Microsoft.Agents.AI.Foundry</c>).
///
/// Credential priority:
///   1. Service principal (TenantId + ClientId + ClientSecret) → ClientSecretCredential
///   2. None → DefaultAzureCredential (Velo Managed Identity)
///
/// API key auth is intentionally NOT supported here. The Foundry Agents/Responses management
/// surface (<c>AIProjectClient.AsAIAgent</c>) is Entra ID (AAD) only — a resource-level API key
/// authenticates model *inference* calls (e.g. <c>/openai/v1/responses</c>) but is rejected with
/// a 403 on the agent-management surface, even when "Allow API key based authentication" is
/// enabled on the resource. Any stored API key on <see cref="AgentConfig"/> is ignored here;
/// see docs/foundry-agent-setup.md for the incident that identified this.
/// </summary>
public static class FoundryClientFactory
{
    /// <summary>Creates an agent using the credential + endpoint fields from an <see cref="AgentConfig"/>.</summary>
    public static AIAgent CreateAgent(AgentConfig config, string instructions)
        => BuildAgent(config.FoundryEndpoint, config.DeploymentName, instructions,
            config.TenantId, config.ClientId, config.ClientSecret);

    /// <summary>Creates an agent given individual credential fields (used by the test-connection path).</summary>
    public static AIAgent BuildAgent(
        string endpoint,
        string deploymentName,
        string instructions,
        string? tenantId = null,
        string? clientId = null,
        string? clientSecret = null)
    {
        var projectClient = BuildProjectClient(endpoint, tenantId, clientId, clientSecret);
        return projectClient.AsAIAgent(
            model: deploymentName,
            name: "Velo Engineering Assistant",
            instructions: instructions);
    }

    /// <summary>Creates the underlying project client — used by the test-connection path, which
    /// needs a lightweight call (listing connections) rather than a full agent invocation.</summary>
    public static AIProjectClient BuildProjectClient(
        string endpoint, string? tenantId, string? clientId, string? clientSecret)
        => new(new Uri(endpoint), BuildCredential(tenantId, clientId, clientSecret));

    private static TokenCredential BuildCredential(string? tenantId, string? clientId, string? clientSecret)
    {
        if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
            return new ClientSecretCredential(tenantId, clientId, clientSecret);

        return new DefaultAzureCredential();
    }
}
