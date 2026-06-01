-- ============================================================
-- Migration 016: Add estimation/approximation flags to DoraMetrics
--
-- These columns allow the dashboard to clearly surface when a
-- DORA metric value is an approximation or proxy rather than
-- a precisely measured figure, so users are never misled.
--
-- IsDeploymentFrequencyEstimated: no deployment-tagged pipelines
--   were detected; DF was computed from all successful
--   runs as a fallback estimate.
--
-- IsLeadTimeApproximate: Lead Time is always the average pipeline
--   build duration (proxy). True PR-merge-to-deploy time is not
--   yet implemented. This column is always 1 for now.
--
-- IsReworkRateEstimated: no work-item state-transition events were
--   available for the period; Rework Rate defaulted to 0.
-- ============================================================

ALTER TABLE DoraMetrics
    ADD IsDeploymentFrequencyEstimated BIT NOT NULL DEFAULT 0,
        IsLeadTimeApproximate          BIT NOT NULL DEFAULT 1,
        IsReworkRateEstimated          BIT NOT NULL DEFAULT 0;
