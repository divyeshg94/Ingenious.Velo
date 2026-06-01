# Changelog

All notable changes to Velo will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Fixed — DORA metric accuracy
- **Rework Rate** no longer uses a nonsensical pipeline-name-count proxy (`(total runs − distinct names) ÷ total runs`).  
  It now measures real work-item churn: items that transitioned from a completed state back to an active state ÷ total completions, sourced from ADO `workitem.updated` service hook events.  
  When no work-item events have been received the metric shows "insufficient data" (returns 0 and sets `IsReworkRateEstimated = true`) rather than a misleading figure.
- **Change Failure Rate** is now restricted to deployment-tagged pipeline runs only (failed deployments ÷ total deployments).  
  Previously it counted every failed pipeline run — including CI builds, lint checks, and flaky tests — inflating the figure significantly.  
  When no deployment-tagged pipelines exist it falls back to all runs and sets `IsChangeFailureRateEstimated = true`.
- **Lead Time for Changes** now clearly documents and labels itself as an approximation (average pipeline build duration) in the code, DTO, and dashboard UI ("approx. build duration").  
  Previously the README claimed "PR merge-to-deploy time" which was never implemented.
- **Deployment Frequency** fallback (when no deployment-tagged pipelines are detected) now surfaces a visible "estimated" badge on the dashboard card so users know the figure is an approximation rather than a true deployment count.

### Changed
- Extracted shared `WorkItemReworkCalculator` static helper (`Velo.Api.Services`) — eliminates the duplicated done/active state sets and rework logic that previously existed in `TeamHealthComputeService` (dead code) and replaces the flawed proxy in `DoraComputeService`.
- `DoraMetricsDto` and `DoraMetrics` entity gain five new boolean fields: `IsDeploymentFrequencyEstimated`, `IsLeadTimeApproximate`, `IsChangeFailureRateEstimated`, `IsMttrEstimated`, `IsReworkRateEstimated`.
- DB migrations `016` and `017` add the corresponding BIT columns to the `DoraMetrics` table.
- `dora.component.html` renders "estimated", "approx. build duration", and "insufficient data" indicator badges on the affected metric cards.
- `dora.component.scss` adds `.tag-estimated`, `.tag-approx`, `.tag-no-data`, and `.m-card__note` styles.
- README updated to accurately describe what each metric measures, including proxy/approximation caveats.

### Tests
- New unit tests in `DoraComputeServiceTests`:
  - Rework Rate is 0 (not high) when a single pipeline runs 100 times with no work-item events.
  - Rework Rate correctly computes done→active churn from work-item events.
  - `IsReworkRateEstimated` is set when no work-item events are present and cleared otherwise.
  - CFR uses deployment runs only when deployment-tagged pipelines exist.
  - CFR falls back to all runs (with `IsDeploymentFrequencyEstimated`) when no deployment-tagged pipelines exist.
  - `IsDeploymentFrequencyEstimated` and `IsLeadTimeApproximate` flags are verified.
  - Rework rate rating thresholds verified using work-item event fixtures.

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
