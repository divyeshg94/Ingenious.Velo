-- Migration 003: PR quality and test stability tables

CREATE TABLE pull_requests (
    id                  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    org_id              NVARCHAR(100)    NOT NULL REFERENCES organizations(org_id),
    project_id          NVARCHAR(100)    NOT NULL,
    ado_pr_id           INT              NOT NULL,
    repository_id       NVARCHAR(100)    NOT NULL,
    title               NVARCHAR(500)    NOT NULL DEFAULT '',
    created_at          DATETIMEOFFSET   NOT NULL,
    completed_at        DATETIMEOFFSET   NULL,
    first_review_at     DATETIMEOFFSET   NULL,
    merged_at           DATETIMEOFFSET   NULL,
    lines_added         INT              NOT NULL DEFAULT 0,
    lines_deleted       INT              NOT NULL DEFAULT 0,
    files_changed       INT              NOT NULL DEFAULT 0,
    comment_count       INT              NOT NULL DEFAULT 0,
    iteration_count     INT              NOT NULL DEFAULT 0,
    is_approved         BIT              NOT NULL DEFAULT 0
);

CREATE INDEX ix_pull_requests_org_project ON pull_requests (org_id, project_id, created_at DESC);

CREATE TABLE team_health (
    id                      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    org_id                  NVARCHAR(100)    NOT NULL REFERENCES organizations(org_id),
    project_id              NVARCHAR(100)    NOT NULL,
    computed_at             DATETIMEOFFSET   NOT NULL DEFAULT SYSDATETIMEOFFSET(),

    -- Cycle time breakdown (hours)
    coding_time_hours       FLOAT            NOT NULL DEFAULT 0,
    review_time_hours       FLOAT            NOT NULL DEFAULT 0,
    merge_time_hours        FLOAT            NOT NULL DEFAULT 0,
    deploy_time_hours       FLOAT            NOT NULL DEFAULT 0,

    -- PR quality
    avg_pr_size_lines       FLOAT            NOT NULL DEFAULT 0,
    pr_comment_density      FLOAT            NOT NULL DEFAULT 0,
    pr_approval_rate        FLOAT            NOT NULL DEFAULT 0,

    -- Test health
    test_pass_rate          FLOAT            NOT NULL DEFAULT 0,
    flaky_test_rate         FLOAT            NOT NULL DEFAULT 0,

    -- Deployment risk score (0.0 = low, 1.0 = high)
    deployment_risk_score   FLOAT            NOT NULL DEFAULT 0
);

CREATE INDEX ix_team_health_org_project ON team_health (org_id, project_id, computed_at DESC);
