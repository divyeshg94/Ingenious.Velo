-- Migration 006: Row-Level Security policies for multi-tenant isolation
-- Every table with an org_id column gets a RLS policy so that DB-level queries
-- are automatically filtered by the current session's org context.

-- Create the security predicate function
CREATE FUNCTION dbo.fn_org_security_predicate(@org_id NVARCHAR(100))
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN SELECT 1 AS result
WHERE SESSION_CONTEXT(N'org_id') = @org_id;
GO

-- Apply RLS to pipeline_runs
CREATE SECURITY POLICY policy_pipeline_runs
ADD FILTER PREDICATE dbo.fn_org_security_predicate(org_id) ON dbo.pipeline_runs,
ADD BLOCK  PREDICATE dbo.fn_org_security_predicate(org_id) ON dbo.pipeline_runs
WITH (STATE = ON);
GO

-- Apply RLS to dora_metrics
CREATE SECURITY POLICY policy_dora_metrics
ADD FILTER PREDICATE dbo.fn_org_security_predicate(org_id) ON dbo.dora_metrics,
ADD BLOCK  PREDICATE dbo.fn_org_security_predicate(org_id) ON dbo.dora_metrics
WITH (STATE = ON);
GO

-- Apply RLS to pull_requests
CREATE SECURITY POLICY policy_pull_requests
ADD FILTER PREDICATE dbo.fn_org_security_predicate(org_id) ON dbo.pull_requests,
ADD BLOCK  PREDICATE dbo.fn_org_security_predicate(org_id) ON dbo.pull_requests
WITH (STATE = ON);
GO

-- Apply RLS to team_health
CREATE SECURITY POLICY policy_team_health
ADD FILTER PREDICATE dbo.fn_org_security_predicate(org_id) ON dbo.team_health,
ADD BLOCK  PREDICATE dbo.fn_org_security_predicate(org_id) ON dbo.team_health
WITH (STATE = ON);
GO

-- Apply RLS to rework_events
CREATE SECURITY POLICY policy_rework_events
ADD FILTER PREDICATE dbo.fn_org_security_predicate(org_id) ON dbo.rework_events,
ADD BLOCK  PREDICATE dbo.fn_org_security_predicate(org_id) ON dbo.rework_events
WITH (STATE = ON);
GO

-- Apply RLS to agent_interactions
CREATE SECURITY POLICY policy_agent_interactions
ADD FILTER PREDICATE dbo.fn_org_security_predicate(org_id) ON dbo.agent_interactions,
ADD BLOCK  PREDICATE dbo.fn_org_security_predicate(org_id) ON dbo.agent_interactions
WITH (STATE = ON);
GO

-- Usage: Before any query, set org context:
-- EXEC sp_set_session_context N'org_id', N'<org-id>';
