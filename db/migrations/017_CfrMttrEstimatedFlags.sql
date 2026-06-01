-- ============================================================
-- Migration 017: Add estimation flags for CFR and MTTR
--
-- These columns allow the dashboard to surface when Change Failure
-- Rate and Mean Time to Restore are computed from all pipeline runs
-- (fallback) rather than deployment-tagged runs only.
--
-- IsChangeFailureRateEstimated: no deployment-tagged pipelines were
--   detected; CFR was computed from all pipeline runs as a fallback.
--   This may overcount failures (build failures ≠ production incidents).
--
-- IsMttrEstimated: no deployment-tagged pipelines were detected;
--   MTTR was computed from all pipeline failures as a fallback.
--   This may include non-production failures in the calculation.
-- ============================================================

ALTER TABLE DoraMetrics
    ADD IsChangeFailureRateEstimated BIT NOT NULL DEFAULT 0,
        IsMttrEstimated              BIT NOT NULL DEFAULT 0;
