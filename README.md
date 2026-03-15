# Velo

**AI-Powered DORA Metrics, Pipeline Analytics, and Engineering Intelligence for Azure DevOps**

[![Marketplace](https://img.shields.io/badge/VS%20Marketplace-Install-blue)](https://marketplace.visualstudio.com/items?itemName=divyeshg94.velo)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

Built on **Microsoft Foundry** + **Azure PaaS**. Native Azure DevOps extension — one-click install, zero configuration.

---

## What Is Velo?

Velo surfaces three integrated entry points inside Azure DevOps:

| Entry Point | What You Get |
|-------------|-------------|
| **Project Hub** | Full DORA metrics dashboard, team health, and Pipeline Intelligence Agent chat |
| **Dashboard Widgets** | Compact DORA metric tiles for any ADO dashboard |
| **Pipeline Task** | `Velo@1` — optional instrumentation step in your YAML pipelines |

### DORA Metrics (all 5, including 2026 Rework Rate)

- Deployment Frequency
- Lead Time for Changes
- Change Failure Rate
- Mean Time to Restore
- **Rework Rate** (2026)

No manual tagging. Velo auto-detects deployment events from your YAML stage names and environment tags.

---

## Architecture

```
VS Marketplace CDN          Azure PaaS (your subscription)
┌──────────────────┐        ┌─────────────────────────────┐
│  Angular 19      │◄──────►│  ASP.NET Core 9 API         │
│  Extension UI    │  Auth   │  Azure Container Apps       │
└──────────────────┘  B2C   ├─────────────────────────────┤
                            │  Azure Functions (ingestion) │
Azure DevOps ──────────────►│  ServiceHookTrigger          │
 (service hooks)            │  MetricsComputeTimer         │
                            ├─────────────────────────────┤
                            │  Azure SQL Serverless        │
                            │  Row-level security (RLS)    │
                            ├─────────────────────────────┤
                            │  Microsoft Foundry Agent     │
                            │  GPT-4o (cached by pipeline) │
                            └─────────────────────────────┘
```

**Estimated cost: ~$23/month at low traffic (0-50 orgs)**

---

## Quick Start

### Install from Marketplace

1. Go to your Azure DevOps organization → **Organization Settings → Extensions**
2. Search for **Velo** or install directly from the [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=divyeshg94.velo)
3. The Velo tab appears in every project's left navigation immediately

### Local Development

See [docs/contributing/local-setup.md](docs/contributing/local-setup.md)

### Deploy Your Own Backend (Enterprise)

See [docs/extension/self-host-backend.md](docs/extension/self-host-backend.md)

---

## Repository Structure

```
velo/
├── src/
│   ├── Velo.Extension/     # Angular 19 ADO Extension
│   ├── Velo.Api/           # ASP.NET Core 9 REST API
│   ├── Velo.Functions/     # Azure Functions (event ingestion + metrics compute)
│   ├── Velo.Agent/         # Microsoft Foundry AI Agent
│   └── Velo.Shared/        # Shared models and contracts
├── infra/                  # Bicep IaC (dev/staging/prod)
├── db/migrations/          # SQL migrations (001–006)
└── docs/                   # Architecture, API, and dev docs
```

---

## License

MIT — see [LICENSE](LICENSE)
