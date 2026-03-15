# Foundry AI Agent Design

## Overview

The Velo Pipeline Intelligence Agent is built on the Microsoft Foundry Agent Framework. It gives engineering teams a natural language interface to their pipeline data.

## Tool Catalog

| Tool | Class | Purpose |
|------|-------|---------|
| `pipeline_analysis` | `PipelineAnalysisTool` | Fetch YAML definitions, build history, stage timing |
| `code_analysis` | `CodeAnalysisTool` | PR size, test stability, code churn |
| `recommendations` | `RecommendationTool` | Structured optimization suggestions (cached) |

## Cost Controls

1. **Response caching** — keyed by SHA-256 of the pipeline YAML definition. Same pipeline = zero extra tokens.
2. **Daily token budget** — 50,000 input tokens per org per day on the free tier. Enforced via `v_daily_token_usage` view and `RateLimitMiddleware`.
3. **Pure SQL DORA metrics** — most users get value without touching the agent. Only pipeline analysis uses Foundry.
4. **Azure Cost Alert** — $30 threshold triggers before token spend becomes significant.

## Observability

All agent interactions are written to `agent_interactions` table and forwarded to the Foundry Control Plane for:
- Evaluation tracing
- Token usage dashboards
- Latency monitoring
- Cache hit rate analysis

## Prompt Files

- `system-prompt.md` — agent identity, capabilities, constraints, tone
- `analysis-templates.md` — structured output templates for consistent, parseable responses
