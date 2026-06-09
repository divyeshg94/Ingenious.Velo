# DORA Metrics Customer Guide

This document explains how Velo calculates DORA metrics, how to interpret results, and what teams can do to move toward Elite performance.

## What DORA Metrics Measure

DORA metrics are industry-standard delivery health indicators:

1. **Deployment Frequency** - how often your team ships to production.
2. **Lead Time for Changes** - how quickly changes move through delivery.
3. **Change Failure Rate** - how often deployments cause failures.
4. **Mean Time to Restore (MTTR)** - how quickly service recovers after failures.
5. **Rework Rate** - how much completed work is reopened or redone.

Velo computes these metrics on a **rolling 30-day window**. On first connect, Velo back-fills up to **60 days** of historical pipeline runs so the rolling window has data immediately; older history is ingested incrementally as new builds complete.

## How Velo Calculates Each Metric

### 1) Deployment Frequency

**Formula used by Velo**  
`Successful deployment runs in last 30 days / 30`

**Fallback behavior**  
If no deployment-tagged pipelines exist, Velo estimates using all successful runs in the same period and marks the metric as **Estimated**.

### 2) Lead Time for Changes

**Preferred formula used by Velo**  
For each merged pull request in the rolling 30-day window, Velo finds the **first successful production deployment** that finishes at or after the PR's merge time, then computes:

`Deployment finish time − PR merge time`

Lead Time is the **average** of those PR→deploy durations (in hours). PR↔deploy pairs older than 60 days are dropped as outliers.

**Fallback behavior**  
When fewer than 3 PR→deploy linkages exist in the window (e.g. PR ingestion hasn't caught up, or the project deploys infrequently), Velo falls back to the **average successful build duration** as a proxy and flags the metric with `IsLeadTimeApproximate = true` so the dashboard can show an "Approximate" badge.

### 3) Change Failure Rate

**Formula used by Velo**  
`Failed deployment runs / Total deployment runs * 100`

**Fallback behavior**  
If no deployment-tagged pipelines exist, Velo uses all runs in the period and marks the metric as **Estimated** (`IsChangeFailureRateEstimated = true`).

### 4) Mean Time to Restore (MTTR)

**Formula used by Velo**  
For each failed deployment run, Velo finds the next successful run of the same pipeline and computes:

`Next success finish time − Failure finish time`

MTTR is the average of those restore times (in hours). Using finish times (rather than start times) means MTTR reflects the time service was actually unhealthy, not the duration of the recovery pipeline itself.

**Fallback behavior**  
If no deployment-tagged pipelines exist, Velo uses all runs in the period and marks the metric as **Estimated** (`IsMttrEstimated = true`).

### 5) Rework Rate

**Formula used by Velo**  
`Work items moved from Done back to Active / Total completions * 100`

This is implemented as work-item state-transition churn and used as the project proxy for unplanned rework.

**Fallback behavior**  
If no work-item events are available in the window, Velo reports 0 and marks the metric as **Estimated / insufficient data**.

## Performance Bands (Low, Medium, High, Elite)

Velo rates metrics against DORA-aligned thresholds:

| Metric | Elite | High | Medium | Low |
|---|---|---|---|---|
| Deployment Frequency | >= 1/day | >= 1/week | >= 1/month | < 1/month |
| Lead Time for Changes | <= 1 hour | <= 1 day | <= 1 week | > 1 week |
| Change Failure Rate | <= 5% | <= 30% | <= 45% | > 45% |
| MTTR | <= 1 hour | <= 1 day | <= 1 week | > 1 week |
| Rework Rate | <= 4% | <= 8% | <= 32% | > 32% |

## How to Improve Your DORA Metrics

### Improve Deployment Frequency
- Break releases into smaller, safer increments.
- Standardize CI/CD templates across teams.
- Use automated quality gates so low-risk changes flow faster.
- Reduce manual handoffs in release approvals.

### Improve Lead Time for Changes
- Keep pull requests small and reviewable.
- Reduce queue time by assigning clear review ownership.
- Parallelize test stages where possible.
- Remove recurring bottlenecks in build and approval workflows.

### Improve Change Failure Rate
- Shift quality checks left (tests, static checks, security scans).
- Use progressive rollout strategies (canary, feature flags, staged rollout).
- Harden release readiness checklists.
- Track top failure causes and eliminate repeat classes of incidents.

### Improve MTTR
- Strengthen alerting and on-call ownership.
- Add clear rollback and recovery runbooks.
- Practice incident response and restoration drills.
- Improve observability (logs, traces, service-level dashboards).

### Improve Rework Rate
- Clarify acceptance criteria before implementation starts.
- Invest in test automation for regression-prone areas.
- Reduce context switching and incomplete handoffs.
- Run post-incident and post-release retrospectives with action tracking.

## Elite Team Practices (What Elite Teams Do)

Teams that reach and sustain Elite performance typically:

- Deploy in small batches many times per week or day.
- Keep cycle time short with fast, predictable review and CI feedback.
- Treat reliability as a release requirement, not a post-release activity.
- Use automated rollback/forward-fix patterns.
- Monitor production health continuously and respond quickly.
- Learn quickly through blameless retrospectives and measurable follow-ups.
- Maintain strong engineering fundamentals: test automation, code review discipline, and pipeline hygiene.

## How to Use This in Customer Reviews

- Focus on **trend direction** over single-point values.
- Compare metrics at the same scope (same project/team and time window).
- Always check whether a metric is marked **Estimated** before making decisions.
- Pair DORA trends with delivery context (release calendars, incidents, major migrations).

## FAQ

**Why can Lead Time look better or worse than expected?**  
Velo prefers PR-merge-to-production lead time. When fewer than 3 PR→deploy linkages are available in the 30-day window — for example, while pull-request ingestion is still catching up after first connect, or on projects with very low deploy frequency — it falls back to the successful-build-duration proxy and flags the metric as **Approximate**. The Approximate badge is the signal to wait a few days or check that the Velo extension has PR read access.

**Why are some metrics marked as Estimated?**  
Estimated status appears when deployment-tagged runs or work-item events are missing, and Velo uses documented fallback logic.

**How does Velo decide which pipelines are "deployments"?**  
Velo tags a pipeline run as a deployment when the pipeline name or any stage name contains one of: `deploy`, `release`, `prod`, `production`, `publish`, `rollout`, `canary`, `promote`, or the whole word `cd` (e.g. `API-CD`). Matching is case-insensitive. If your team uses different terminology, set `IsDeployment` explicitly via the `Velo@1` pipeline task to short-circuit the heuristic.

**Should we optimize one metric first?**  
Start with the biggest bottleneck in your flow. Most teams gain fastest by improving review queues, deployment automation, and incident recovery readiness.
