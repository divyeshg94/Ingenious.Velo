# Changelog

All notable changes to Velo will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] – 2026-04-09

### Added
- **DORA Metrics dashboard** — automatic tracking of all five metrics (Deployment Frequency, Lead Time for Changes, Change Failure Rate, Mean Time to Restore, and Rework Rate) sourced directly from Azure DevOps pipelines and pull requests
- **Pipeline Intelligence Agent** — AI-powered Q&A over delivery health, backed by Microsoft Foundry and Azure OpenAI GPT-4o; responses cached by pipeline definition SHA-256
- **Dashboard widgets** — DORA tile and Team Health tile embeddable in any Azure DevOps dashboard
- **Service hook ingestion** — event-driven capture of pipeline runs, pull request lifecycle, and deployment events via Azure DevOps webhooks
- **Multi-tenant data isolation** — row-level security enforced in Azure SQL Server via `SESSION_CONTEXT(N'org_id')`
- **Free tier rate limiting** — 50,000 input tokens per organisation per day enforced by `RateLimitMiddleware` and the `v_daily_token_usage` view
- **Velo pipeline task** (`Velo@1`) — YAML pipeline task for explicit instrumentation of custom deployment stages
- **`vso.analytics` scope** — reads pipeline analytics data for enriched DORA computation
- **Application Insights integration** — install-level telemetry and error capture on the backend API
- **GitHub Actions CI** — runs `.NET` tests, Angular lint/build, and `tfx-cli` extension package validation on every pull request
- **MIT License** — open-source under MIT for community contributions and EB-1A evidence trail

### Infrastructure
- ASP.NET Core 9 REST API on Azure Container Apps (Model B)
- Azure Functions v4 (.NET 9 isolated) for timer-triggered DORA computation
- Bicep IaC modules with dev / staging / prod parameter files
- SQL migrations `001`–`006` including row-level security and token usage views

[1.0.0]: https://github.com/divyeshg94/Ingenious.Velo/releases/tag/v1.0.0
