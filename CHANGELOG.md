# Changelog

All notable changes to Velo will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Fixed — extension reliability and support discoverability
- Extension production API base URL now resolves to the managed API endpoint (`https://api.getvelo.dev`) with runtime local override support (`velo-api-base-url` / legacy `api-base-url`) to prevent post-update outages when backend hostnames change.
- Settings now include an **API Connection** override/reset panel so organizations can recover from endpoint misconfiguration without reinstalling the extension.
- Added first-class **Support** and **Docs** links in the extension navigation and settings page to make creator contact paths immediately visible.

### Fixed — DORA computation correctness, ingestion completeness, and runtime bugs (PR #31)
- **Lead Time for Changes** now measures real PR-merge → first-successful-deployment time when at least 3 PR↔deploy linkages exist in the 30-day window (averaged in hours, outliers > 60 days dropped). Falls back to the previous build-duration proxy with `IsLeadTimeApproximate = true` only when linkage data is too sparse.
- **MTTR** now uses pipeline **finish** times instead of start times when measuring restore duration (`nextSuccess.FinishTime − failure.FinishTime`). The previous formula under-reported MTTR by the duration of the recovery pipeline itself.
- **DORA metrics persistence** now upserts one row per `(OrgId, ProjectId, ComputedDate)` UTC day, atomically. A new `ComputedDate` (date) column plus `UX_DoraMetrics_OrgId_ProjectId_ComputedDate` unique index guarantees the one-row-per-day invariant under concurrent webhook recomputes; `SaveAsync` retries on SqlException 2627/2601 by detaching and re-reading. Previously every webhook inserted a new row, bloating the `DoraMetrics` table.
- **30-day rolling window** is now enforced at the repository via a new period-based query (`GetRunsInPeriodAsync`) with no page cap. The previous code path was capped at 500 runs per query, silently dropping data for busy orgs.
- **ADO build ingest** now paginates via the `x-ms-continuationtoken` header (bounded by a 60-day lookback and 25-page safety ceiling, short-circuits when an entire page is already stored). Previously capped at 200 builds on first sync, which truncated history for any customer with more than 200 builds in the window.
- **DoraController auto-recovery** background `Task.Run` now resolves services from a fresh `IServiceScope` and calls the new `TenantContextHelper.SetAsync` so both EF query filters and SQL Server `SESSION_CONTEXT(N'org_id')` are populated. Previously threw `ObjectDisposedException` once the request scope was torn down and ran without RLS.
- **PR event projection** no longer drops `FilesChanged`, `LinesAdded/Deleted`, `FirstApprovedAt`, `CycleDurationMinutes`, and reviewer fields. TeamHealth and the Foundry agent now see real PR data again.

### Changed
- New shared `DeploymentDetector` heuristic used by both webhook and ingest paths. Keyword set expanded to: `deploy`, `release`, `prod`, `production`, `publish`, `rollout`, `canary`, `promote`, plus whole-word `cd` (e.g. `API-CD`). Customer-facing docs updated accordingly.
- New shared `TenantContextHelper` consolidates the EF + SQL Server RLS session-context setup; `WebhookController.SetTenantContextAsync` and the DoraController background scope both delegate to it.
- `ComputeLeadTimeFromPrAndDeploys` rewritten from O(PR × Deployments) `FirstOrDefault` scan to a two-pointer linear walk after sorting merged PRs by `ClosedAt` — O(PR + Deployments).
- All user-supplied log arguments (orgId, projectId, repository name, URL, build numbers) are now wrapped in `LogSanitizer.SanitiseForLog(...)` to satisfy CodeQL log-injection rules.

### Documentation
- `docs/DORA_Metrics_Customer_Guide.md` updated to reflect the real Lead Time formula (PR merge → first successful deploy with build-duration fallback), the corrected MTTR formula (finish-time-based), the 60-day first-connect back-fill, and a new FAQ entry explaining how Velo decides which pipelines are "deployments".
- `README.md` DORA section updated to drop the "PR-merge-to-deploy time not yet implemented" caveat for Lead Time and clarify the MTTR finish-time formula.
- `.github/copilot-instructions.md` adds an explicit "sanitize every user-supplied log argument" rule and a new "Background work" subsection under Multi-Tenancy describing when to call `TenantContextHelper`.

---

## [0.x.0] - Previous Unreleased entries

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
