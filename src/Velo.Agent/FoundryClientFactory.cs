using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;

namespace Velo.Agent;

/// <summary>
/// Central factory for creating <see cref="PersistentAgentsClient"/> instances.
///
/// Credential priority:
///   1. API key  → <c>api-key</c> HTTP header via <see cref="ApiKeyHeaderPolicy"/>
///   2. Service principal (TenantId + ClientId + ClientSecret) → ClientSecretCredential
///   3. None     → DefaultAzureCredential (Velo Managed Identity)
///
/// Why a custom policy instead of a TokenCredential shim for API key auth:
///   Azure AI Foundry / AzureML agent endpoints authenticate via the <c>api-key</c>
///   request header, NOT via a Bearer token in <c>Authorization</c>.
///   Sending the key as a Bearer token results in a 403 "Identity(object id: ) does not
///   have permissions" because the service tries to decode it as an Azure AD JWT,
///   extracts an empty <c>oid</c> claim, and rejects it.
///   The <see cref="ApiKeyHeaderPolicy"/> strips any SDK-injected Authorization header
///   and injects <c>api-key: &lt;value&gt;</c> so the service receives the correct
///   credential in the expected header.
/// </summary>
public static class FoundryClientFactory
{
    /// <summary>Creates a client using all credential fields from an <see cref="AgentConfig"/>.</summary>
    public static PersistentAgentsClient Create(AgentConfig config)
        => Build(config.FoundryEndpoint, config.ApiKey, config.TenantId, config.ClientId, config.ClientSecret);

    /// <summary>Creates a client given individual credential fields (used by the test-connection path).</summary>
    public static PersistentAgentsClient Build(
        string endpoint,
        string? apiKey,
        string? tenantId = null,
        string? clientId = null,
        string? clientSecret = null)
    {
        // Option 1: API key — inject via api-key header, not as Bearer token
        if (!string.IsNullOrEmpty(apiKey))
        {
            var opts = new PersistentAgentsAdministrationClientOptions();
            opts.AddPolicy(new ApiKeyHeaderPolicy(apiKey), HttpPipelinePosition.BeforeTransport);
            // NoOpCredential satisfies the non-null constructor requirement;
            // its token is stripped by ApiKeyHeaderPolicy before the wire.
            return new PersistentAgentsClient(endpoint, new NoOpCredential(), opts);
        }

        // Option 2: Service principal (cross-tenant customer Foundry resource)
        if (!string.IsNullOrEmpty(tenantId)
            && !string.IsNullOrEmpty(clientId)
            && !string.IsNullOrEmpty(clientSecret))
            return new PersistentAgentsClient(endpoint,
                new ClientSecretCredential(tenantId, clientId, clientSecret));

        // Option 3: Velo Managed Identity (default, resource in Velo's subscription)
        return new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential())
            .GetPersistentAgentsClient();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Strips the SDK-injected <c>Authorization</c> header and injects the Foundry
    /// resource key via the <c>api-key</c> header that the Azure AI Agents REST API expects.
    /// Runs at <see cref="HttpPipelinePosition.BeforeTransport"/> — after the credential
    /// policy has already added its header — so the replacement is guaranteed.
    /// </summary>
    private sealed class ApiKeyHeaderPolicy(string apiKey) : HttpPipelinePolicy
    {
        public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            SetApiKeyHeader(message);
            ProcessNext(message, pipeline);
        }

        public override ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            SetApiKeyHeader(message);
            return ProcessNextAsync(message, pipeline);
        }

        private void SetApiKeyHeader(HttpMessage message)
        {
            message.Request.Headers.Remove("Authorization");
            message.Request.Headers.SetValue("api-key", apiKey);
        }
    }

    /// <summary>
    /// Placeholder <see cref="TokenCredential"/> required by the SDK constructor when
    /// API key auth is chosen. Its token is immediately stripped by
    /// <see cref="ApiKeyHeaderPolicy"/> and never reaches the wire.
    /// </summary>
    private sealed class NoOpCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("no-op", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(new AccessToken("no-op", DateTimeOffset.MaxValue));
    }
}
