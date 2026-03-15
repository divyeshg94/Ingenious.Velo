# Velo Architecture Overview

Velo uses a two-layer architecture:

1. **Extension UI** — served from the Visual Studio Marketplace CDN at zero hosting cost
2. **Backend services** — consumption-based Azure PaaS (pay only for actual usage)

## Components

| Component | Technology | Notes |
|-----------|-----------|-------|
| Extension UI | Angular 19 + VSS SDK 2.0 | Hub, Dashboard widgets, Pipeline task |
| API Layer | ASP.NET Core 9 on Azure Container Apps | Scales to zero |
| Data Ingestion | Azure Functions (consumption) | Event-driven, billed per execution |
| Database | Azure SQL Serverless | Auto-pause after 60min idle |
| AI Agent | Microsoft Foundry + GPT-4o | Cached by pipeline fingerprint |
| Auth | Azure AD B2C + ADO OAuth | SSO with existing ADO identity |
| Secrets | Azure Key Vault + Managed Identity | No connection strings in code |
| Observability | Azure Monitor + Application Insights | All Foundry interactions traced |

## Deployment Models

- **Model B (Phase 1-2):** Extension on MS CDN + backend on your Azure subscription
- **Model C (Phase 3+):** Customer deploys backend to their own Azure via Bicep template

See [data-flow.md](data-flow.md) for the end-to-end event pipeline.
