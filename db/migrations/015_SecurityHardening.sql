-- ============================================================
-- Migration 015: Security Hardening
--
-- 1. Add AadTenantId to Organizations
--    Stores the AAD tenant GUID extracted from the JWT 'tid'
--    claim on first authentication. Subsequent requests from
--    a different tenant to the same org are rejected in
--    TenantResolutionMiddleware (anti-header-spoofing).
--
-- 2. Add ModifiedDate to Organizations
--    Tracks when TenantResolutionMiddleware last updated the
--    tenant binding (or any other modification).
--
-- 3. Row-Level Security policies
--    Adds SQL Server RLS FILTER and BLOCK predicates on all
--    tenant-scoped tables so that direct DB connections
--    (outside EF Core) are also scoped to the session org_id.
-- ============================================================

-- ─── 1. Organizations — new columns ────────────────────────────────────────

ALTER TABLE Organizations
    ADD AadTenantId  NVARCHAR(100) NULL,
        ModifiedDate DATETIMEOFFSET NULL;

GO

-- ─── 2. Row-Level Security ─────────────────────────────────────────────────
-- The predicate function reads the session context value set by
-- TenantResolutionMiddleware / WebhookController via sp_set_session_context.
-- A NULL session context means no org is bound → no rows are visible (fail-closed).

CREATE OR ALTER FUNCTION dbo.fn_rls_org_predicate(@OrgId NVARCHAR(100))
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS fn_result
    WHERE @OrgId = CAST(SESSION_CONTEXT(N'org_id') AS NVARCHAR(100));

GO

-- Apply FILTER (SELECT/UPDATE/DELETE) and BLOCK (INSERT) predicates.
-- Drop existing policies first so this migration is re-runnable.

IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'rls_PipelineRuns')
    DROP SECURITY POLICY rls_PipelineRuns;

CREATE SECURITY POLICY rls_PipelineRuns
    ADD FILTER PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.PipelineRuns,
    ADD BLOCK  PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.PipelineRuns AFTER INSERT
    WITH (STATE = ON, SCHEMABINDING = ON);

GO

IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'rls_DoraMetrics')
    DROP SECURITY POLICY rls_DoraMetrics;

CREATE SECURITY POLICY rls_DoraMetrics
    ADD FILTER PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.DoraMetrics,
    ADD BLOCK  PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.DoraMetrics AFTER INSERT
    WITH (STATE = ON, SCHEMABINDING = ON);

GO

IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'rls_TeamHealth')
    DROP SECURITY POLICY rls_TeamHealth;

CREATE SECURITY POLICY rls_TeamHealth
    ADD FILTER PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.TeamHealth,
    ADD BLOCK  PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.TeamHealth AFTER INSERT
    WITH (STATE = ON, SCHEMABINDING = ON);

GO

IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'rls_PullRequestEvents')
    DROP SECURITY POLICY rls_PullRequestEvents;

CREATE SECURITY POLICY rls_PullRequestEvents
    ADD FILTER PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.PullRequestEvents,
    ADD BLOCK  PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.PullRequestEvents AFTER INSERT
    WITH (STATE = ON, SCHEMABINDING = ON);

GO

IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'rls_TeamMappings')
    DROP SECURITY POLICY rls_TeamMappings;

CREATE SECURITY POLICY rls_TeamMappings
    ADD FILTER PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.TeamMappings,
    ADD BLOCK  PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.TeamMappings AFTER INSERT
    WITH (STATE = ON, SCHEMABINDING = ON);

GO

IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'rls_WorkItemEvents')
    DROP SECURITY POLICY rls_WorkItemEvents;

CREATE SECURITY POLICY rls_WorkItemEvents
    ADD FILTER PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.WorkItemEvents,
    ADD BLOCK  PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.WorkItemEvents AFTER INSERT
    WITH (STATE = ON, SCHEMABINDING = ON);

GO

IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'rls_AgentConfigurations')
    DROP SECURITY POLICY rls_AgentConfigurations;

CREATE SECURITY POLICY rls_AgentConfigurations
    ADD FILTER PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.AgentConfigurations,
    ADD BLOCK  PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.AgentConfigurations AFTER INSERT
    WITH (STATE = ON, SCHEMABINDING = ON);

GO

IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'rls_ProjectMappings')
    DROP SECURITY POLICY rls_ProjectMappings;

CREATE SECURITY POLICY rls_ProjectMappings
    ADD FILTER PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.ProjectMappings,
    ADD BLOCK  PREDICATE dbo.fn_rls_org_predicate(OrgId) ON dbo.ProjectMappings AFTER INSERT
    WITH (STATE = ON, SCHEMABINDING = ON);

GO

-- ─── 3. DBA bypass view (ops/support access without RLS) ───────────────────
-- Create a dedicated schema-bound view for DBAs to query across all orgs
-- without triggering RLS. Access must be granted explicitly — the default
-- application login must NOT have SELECT on this view.
CREATE OR ALTER VIEW dbo.vw_AllOrgs_PipelineRuns
WITH SCHEMABINDING
AS
    SELECT * FROM dbo.PipelineRuns;

GO
