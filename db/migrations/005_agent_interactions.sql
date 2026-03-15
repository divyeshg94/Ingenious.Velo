-- Migration 005: Foundry agent interaction log
-- Used for observability, evaluation tracing, and token budget enforcement

CREATE TABLE agent_interactions (
    id                  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    org_id              NVARCHAR(100)    NOT NULL REFERENCES organizations(org_id),
    project_id          NVARCHAR(100)    NOT NULL,
    session_id          UNIQUEIDENTIFIER NOT NULL,
    user_message        NVARCHAR(MAX)    NOT NULL,
    agent_response      NVARCHAR(MAX)    NOT NULL,
    tools_called        NVARCHAR(MAX)    NULL, -- JSON array of tool names
    input_tokens        INT              NOT NULL DEFAULT 0,
    output_tokens       INT              NOT NULL DEFAULT 0,
    latency_ms          INT              NOT NULL DEFAULT 0,
    cached              BIT              NOT NULL DEFAULT 0,
    pipeline_hash       NVARCHAR(64)     NULL, -- SHA-256 of pipeline def for cache keying
    created_at          DATETIMEOFFSET   NOT NULL DEFAULT SYSDATETIMEOFFSET()
);

CREATE INDEX ix_agent_interactions_org ON agent_interactions (org_id, created_at DESC);
CREATE INDEX ix_agent_interactions_session ON agent_interactions (session_id, created_at);

-- Daily token usage view for budget enforcement
CREATE VIEW v_daily_token_usage AS
SELECT
    org_id,
    CAST(created_at AT TIME ZONE 'UTC' AS DATE) AS usage_date,
    SUM(input_tokens + output_tokens) AS total_tokens,
    SUM(CASE WHEN cached = 1 THEN 1 ELSE 0 END) AS cached_hits,
    COUNT(*) AS interaction_count
FROM agent_interactions
GROUP BY org_id, CAST(created_at AT TIME ZONE 'UTC' AS DATE);
