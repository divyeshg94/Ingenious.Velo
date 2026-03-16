# Velo — AI-Powered Engineering Intelligence for Azure DevOps

**Measure what matters. Ship with confidence.**

Velo is the only Azure DevOps extension that computes all **five 2026 DORA metrics** natively — no third-party tools, no data exports, no context switching. Just install, connect your pipelines, and start improving.

---

## Why Velo?

Engineering teams using Azure DevOps lack a unified view of delivery performance. Velo solves this by turning your existing pipeline, PR, and work-item data into actionable intelligence — directly inside Azure DevOps.

| Without Velo | With Velo |
|---|---|
| Manually tracking deployment frequency in spreadsheets | Real-time DORA dashboard inside every project |
| No visibility into lead time or change failure rate | All five 2026 DORA metrics computed automatically |
| Guessing which pipelines need optimization | AI-powered recommendations per pipeline |
| Context switching to external analytics tools | Everything lives natively in Azure DevOps |

---

## Features

### 📊 Full DORA Metrics Dashboard

Track all five industry-standard DORA metrics with automatic Elite / High / Medium / Low ratings:

- **Deployment Frequency** — How often your team ships to production
- **Lead Time for Changes** — Time from first commit to production deployment
- **Change Failure Rate** — Percentage of deployments causing rollbacks or hotfixes
- **Mean Time to Restore (MTTR)** — How fast you recover from failures
- **Rework Rate** *(2026 addition)* — Percentage of deployments followed by rework within 48 hours

Metrics are computed hourly from your real pipeline data — zero manual input required.

### 🤖 Foundry AI Agent

Ask natural-language questions about your pipelines and get structured optimization recommendations:

- *"Why is the deploy stage slow in my release pipeline?"*
- *"Which pipelines have the highest failure rate this sprint?"*
- *"How can I reduce lead time for the payments service?"*

The agent analyzes your YAML definitions, build history, stage timing, PR metrics, and test stability to give you actionable answers — not generic advice.

### 📈 Dashboard Widget

Add a compact DORA metrics tile to any Azure DevOps dashboard for at-a-glance visibility. Supports 1×2 and 2×2 sizes.

### ⚡ Pipeline Task (`Velo@1`)

Add the `Velo@1` task to any YAML pipeline to explicitly tag deployments. This improves deployment detection accuracy beyond the default stage-name heuristics.

```yaml
steps:
  - task: Velo@1
    displayName: 'Report deployment to Velo'
```

### 🏥 Team Health Insights

Beyond DORA, Velo tracks operational health signals:

- **Cycle time breakdown** — Coding → Review → Merge → Deploy
- **PR quality** — Average size, comment density, approval rate
- **Test health** — Pass rate and flaky test trends
- **Deployment risk score** — A 0–1 composite risk indicator

---

## How It Works

1. **Install** the extension from the Visual Studio Marketplace
2. **Connect** — Velo automatically ingests data from your pipelines, pull requests, and work items via Azure DevOps service hooks
3. **View** — Open the Velo hub in any project to see your DORA metrics, team health, and AI insights
4. **Improve** — Use the Foundry AI agent to get pipeline-specific optimization recommendations

No agents to install. No infrastructure to manage. Works with your existing Azure Pipelines — YAML and Classic.

---

## Deployment Detection

Velo automatically identifies production deployments using:

- **Stage name patterns** — `deploy`, `release`, `prod`, `production`, `publish` (case-insensitive)
- **Explicit tagging** — Add the `Velo@1` pipeline task for precise control

---

## Security & Privacy

- **Multi-tenant by design** — Every query is scoped to your organization. Row-level security is enforced at both application and database layers.
- **No secrets stored in code** — All credentials use Azure Managed Identity and Key Vault.
- **Data stays in your Azure tenant** — Velo runs on Azure Container Apps and Azure SQL in your region.
- **Scopes requested** — `vso.build` (read pipelines), `vso.code` (read PRs), `vso.work` (read work items). No write access required.

---

## Getting Started

1. Install the extension from the [Visual Studio Marketplace](https://marketplace.visualstudio.com/)
2. Navigate to your Azure DevOps project → **Velo** (appears in the project hub)
3. Your DORA metrics begin populating within one hour of the first pipeline run

---

## Requirements

- Azure DevOps Services (cloud) — any plan
- Pipelines with at least one production deployment stage

---

## Support & Feedback

- 🐛 [Report an issue](https://github.com/divyeshg94/velo/issues)
- 💡 [Request a feature](https://github.com/divyeshg94/velo/issues)
- 📖 [Documentation](https://github.com/divyeshg94/velo/tree/main/docs)

---

## License

Velo is open-source under the [MIT License](https://github.com/divyeshg94/velo/blob/main/LICENSE).
