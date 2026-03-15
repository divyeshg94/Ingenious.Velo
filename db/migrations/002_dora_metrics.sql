-- Migration 002: DORA metrics computed table + indexes

CREATE TABLE dora_metrics (
    id                          UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    org_id                      NVARCHAR(100)    NOT NULL REFERENCES organizations(org_id),
    project_id                  NVARCHAR(100)    NOT NULL,
    computed_at                 DATETIMEOFFSET   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    period_start                DATETIMEOFFSET   NOT NULL,
    period_end                  DATETIMEOFFSET   NOT NULL,

    -- Metric 1: Deployment Frequency (deployments/day)
    deployment_frequency        FLOAT            NOT NULL DEFAULT 0,
    deployment_frequency_rating NVARCHAR(10)     NOT NULL DEFAULT 'Low',

    -- Metric 2: Lead Time for Changes (hours)
    lead_time_hours             FLOAT            NOT NULL DEFAULT 0,
    lead_time_rating            NVARCHAR(10)     NOT NULL DEFAULT 'Low',

    -- Metric 3: Change Failure Rate (0-100%)
    change_failure_rate         FLOAT            NOT NULL DEFAULT 0,
    change_failure_rating       NVARCHAR(10)     NOT NULL DEFAULT 'Low',

    -- Metric 4: Mean Time to Restore (hours)
    mttr_hours                  FLOAT            NOT NULL DEFAULT 0,
    mttr_rating                 NVARCHAR(10)     NOT NULL DEFAULT 'Low',

    -- Metric 5: Rework Rate (2026 addition, 0-100%)
    rework_rate                 FLOAT            NOT NULL DEFAULT 0,
    rework_rate_rating          NVARCHAR(10)     NOT NULL DEFAULT 'Low'
);

-- DORA ratings: Elite, High, Medium, Low
-- Deployment Frequency: Elite=multiple/day, High=1/day, Medium=1/week, Low=1/month
-- Lead Time:           Elite=<1h, High=<1d, Medium=<1w, Low=<1mo
-- Change Failure Rate: Elite=0-5%, High=5-10%, Medium=10-15%, Low=>15%
-- MTTR:                Elite=<1h, High=<1d, Medium=<1w, Low=>1w
-- Rework Rate:         Elite=<5%, High=5-10%, Medium=10-20%, Low=>20%

CREATE INDEX ix_dora_metrics_org_project ON dora_metrics (org_id, project_id, computed_at DESC);
