-- Migration 001: Initial schema
-- Core tables: organizations, pipeline_runs, work_items

CREATE TABLE organizations (
    org_id          NVARCHAR(100)   NOT NULL PRIMARY KEY,
    org_url         NVARCHAR(500)   NOT NULL,
    display_name    NVARCHAR(200)   NOT NULL DEFAULT '',
    is_premium      BIT             NOT NULL DEFAULT 0,
    daily_token_budget INT          NOT NULL DEFAULT 50000,
    registered_at   DATETIMEOFFSET  NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    last_seen_at    DATETIMEOFFSET  NULL
);

CREATE TABLE pipeline_runs (
    id              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    org_id          NVARCHAR(100)    NOT NULL REFERENCES organizations(org_id),
    project_id      NVARCHAR(100)    NOT NULL,
    ado_pipeline_id INT              NOT NULL,
    pipeline_name   NVARCHAR(200)    NOT NULL DEFAULT '',
    run_number      NVARCHAR(50)     NOT NULL DEFAULT '',
    result          NVARCHAR(50)     NOT NULL, -- succeeded, failed, canceled, partiallySucceeded
    start_time      DATETIMEOFFSET   NOT NULL,
    finish_time     DATETIMEOFFSET   NULL,
    duration_ms     BIGINT           NULL,
    is_deployment   BIT              NOT NULL DEFAULT 0,
    stage_name      NVARCHAR(200)    NULL,
    triggered_by    NVARCHAR(200)    NULL,
    ingested_at     DATETIMEOFFSET   NOT NULL DEFAULT SYSDATETIMEOFFSET()
);

CREATE INDEX ix_pipeline_runs_org_project ON pipeline_runs (org_id, project_id, start_time DESC);
CREATE INDEX ix_pipeline_runs_deployment ON pipeline_runs (org_id, project_id, is_deployment, start_time DESC);

CREATE TABLE work_items (
    id              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    org_id          NVARCHAR(100)    NOT NULL REFERENCES organizations(org_id),
    project_id      NVARCHAR(100)    NOT NULL,
    ado_work_item_id INT             NOT NULL,
    work_item_type  NVARCHAR(100)    NOT NULL,
    state           NVARCHAR(100)    NOT NULL,
    state_changed_at DATETIMEOFFSET  NOT NULL,
    created_at      DATETIMEOFFSET   NOT NULL,
    closed_at       DATETIMEOFFSET   NULL
);

CREATE INDEX ix_work_items_org_project ON work_items (org_id, project_id, state_changed_at DESC);
