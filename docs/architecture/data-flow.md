# Data Flow: ADO Service Hook to Metrics

```
Azure DevOps
    │  pipeline completed / PR merged / work item updated
    ▼
WebhookController (ASP.NET Core API - POST /api/webhook/ado)
    │  validate HMAC-SHA256 signature → normalize payload → write to Azure SQL
    ▼
pipeline_runs / pull_requests / work_items tables
    │  (row-level security: filtered by org_id via SESSION_CONTEXT)
    ▼
DoraComputeService (called inline after ingestion)
    │  pure SQL aggregation, zero AI cost
    ▼
dora_metrics / team_health tables
    ▼
ASP.NET Core API (Azure Container Apps)
    │  /api/dora/metrics, /api/health
    ▼
Angular Extension (VS Marketplace CDN)
    │  authenticated via Azure AD B2C token
    ▼
User's Azure DevOps dashboard
```

## AI Agent Path (on demand)

```
User types question in Agent chat tab
    ▼
Angular → POST /api/agent/chat
    ▼
AgentController → AgentService → Velo.Agent (VeloAgent)
    │  check token budget (v_daily_token_usage view)
    │  check cache (pipeline_hash)
    ▼
Microsoft Foundry Agent
    │  tool calls: PipelineAnalysisTool, CodeAnalysisTool, RecommendationTool
    │  each tool queries SQL for the authorized org/project
    ▼
Structured response cached by pipeline_hash
    ▼
agent_interactions row written (tokens, latency, cached flag)
    ▼
Response streamed back to Angular chat
```
