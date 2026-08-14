# Foundry AI Agent — Setup & Troubleshooting

How to connect Velo's AI Agent tab to your Azure AI Foundry project, and how to fix the errors
you'll hit if something's misconfigured.

## Quick setup

1. **Get your project endpoint.** Microsoft Foundry portal (ai.azure.com) → your project →
   Overview → **Project endpoint**. Copy it exactly — don't type it by hand.

   ```
   https://<hub>.services.ai.azure.com/api/projects/<project>
   ```

   A bare hub endpoint (`https://<hub>.services.ai.azure.com`, no `/api/projects/…`) looks
   valid but 404s on every agent call. This is the single most common setup mistake.

2. **Get your model deployment name.** Same portal → your project → **Deployments**. Must
   match exactly (e.g. `gpt-4o`, `gpt-4o-mini`) — it's case-sensitive and typo-sensitive.

3. **Choose authentication.** The Foundry Agents API is **Entra ID (AAD) only** — API keys
   are not supported here, even on resources with "Allow API key based authentication"
   enabled (that setting only governs model inference endpoints like
   `/openai/v1/responses`, not agent calls). Pick one:

   - **Managed Identity** (default) — works if the Foundry resource is in Velo's own
     subscription. Grant Velo's managed identity the **Azure AI User** role on the resource.
   - **Service Principal** — for cross-tenant or customer-owned Foundry resources. Create an
     Entra ID app registration, generate a client secret, and grant that app the
     **Azure AI User** role on the resource (Azure Portal → resource → Access control (IAM)
     → Add role assignment).

4. **Test Connection**, then **Connect Agent**.

## Common errors

### 404 — Resource not found

| Cause | Fix |
|---|---|
| Bare hub endpoint (missing `/api/projects/<project>`) | Copy the full project endpoint from the portal — see step 1 above. |
| Azure OpenAI endpoint (`*.openai.azure.com`) | Not compatible with the Agents API. Use the Foundry project endpoint instead. |
| Model Deployment Name doesn't match | Check Foundry portal → your project → Deployments for the exact name. |

### 403 — Authentication failed

Always means the configured identity lacks access — **not** a wrong API key, because API
keys aren't used for agent calls at all.

Fix: confirm the Managed Identity or Service Principal has the **Azure AI User** role on the
Foundry resource (Azure Portal → resource → Access control (IAM) → Role assignments). This is
separate from the resource's "Allow API key based authentication" setting, which does not
affect agent calls.

### 400 — `Invalid 'response_id': 'assistants'. Expected an ID that begins with 'resp'.`

Seen when a client built against the legacy Assistants API (`Azure.AI.Agents.Persistent`,
threads/runs/assistants routes) is pointed at a Foundry project that only serves the newer
Responses API. The request lands on `/openai/v1/responses/assistants` and Foundry parses
`assistants` as a response ID.

Fix: this shouldn't occur with the current backend (migrated to
`Microsoft.Agents.AI.Foundry` / `AIProjectClient.AsAIAgent`, which targets the Responses
protocol directly). If you see this again, the backend build is stale — redeploy.

### 429 — Rate limit exceeded

Foundry resource hit its request quota. Wait and retry; if it recurs often, check your
deployment's tokens-per-minute (TPM) quota in the Foundry portal.

## Why API keys don't work here

Azure AI Foundry resources expose two separate surfaces:

- **Model inference** (`/openai/v1/responses`, chat completions) — supports API key auth via
  the `api-key` header when "Allow API key based authentication" is enabled on the resource.
- **Agents management** (`AIProjectClient` / `AsAIAgent`, what Velo uses to run the agent) —
  Entra ID (AAD) only, regardless of that setting.

If you can `curl` your inference endpoint successfully with an API key but Velo's Test
Connection still fails with 403, this is why — switch to Service Principal or Managed
Identity, you don't need to keep debugging the key.

## Reference

- [Microsoft Foundry SDK overview](https://learn.microsoft.com/en-us/azure/foundry/how-to/develop/sdk-overview)
- [Microsoft Foundry model provider (Agent Framework)](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/microsoft-foundry)
- Backend implementation: [`FoundryClientFactory.cs`](../../Velo.Agent/FoundryClientFactory.cs), [`VeloAgent.cs`](../../Velo.Agent/VeloAgent.cs)
