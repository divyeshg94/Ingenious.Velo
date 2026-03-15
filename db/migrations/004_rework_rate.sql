-- Migration 004: 2026 rework rate tracking schema
-- Rework rate = percentage of deployments that required a follow-up hotfix/rollback within 48h

CREATE TABLE rework_events (
    id                  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    org_id              NVARCHAR(100)    NOT NULL REFERENCES organizations(org_id),
    project_id          NVARCHAR(100)    NOT NULL,
    original_run_id     UNIQUEIDENTIFIER NOT NULL REFERENCES pipeline_runs(id),
    rework_run_id       UNIQUEIDENTIFIER NOT NULL REFERENCES pipeline_runs(id),
    rework_type         NVARCHAR(50)     NOT NULL, -- hotfix, rollback, revert
    detected_at         DATETIMEOFFSET   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    time_to_rework_hours FLOAT           NOT NULL
);

CREATE INDEX ix_rework_events_org_project ON rework_events (org_id, project_id, detected_at DESC);
CREATE INDEX ix_rework_events_original_run ON rework_events (original_run_id);
