-- ============================================================
-- Migration 014: WorkItemEvents table
--
-- Stores ADO workitem.updated state-transition events so that
-- rework rate can be computed from real defect-fix cycles
-- instead of the build re-run proxy used in Phase 1.
--
-- A "rework" transition = OldState in {done states}
--                         AND NewState in {active states}
-- ============================================================

CREATE TABLE WorkItemEvents
(
    Id            UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID()  PRIMARY KEY,
    OrgId         NVARCHAR(100)    NOT NULL,
    ProjectId     NVARCHAR(100)    NOT NULL,
    WorkItemId    INT              NOT NULL,
    WorkItemType  NVARCHAR(100)    NULL,
    OldState      NVARCHAR(100)    NULL,
    NewState      NVARCHAR(100)    NULL,
    ChangedAt     DATETIMEOFFSET   NOT NULL,
    IngestedAt    DATETIMEOFFSET   NOT NULL DEFAULT SYSUTCDATETIME(),

    -- Auditable columns (matches AuditableEntity)
    CreatedBy     NVARCHAR(200)    NULL,
    CreatedDate   DATETIMEOFFSET   NOT NULL DEFAULT SYSUTCDATETIME(),
    ModifiedBy    NVARCHAR(200)    NULL,
    ModifiedDate  DATETIMEOFFSET   NULL,
    IsDeleted     BIT              NOT NULL DEFAULT 0
);

-- Performance: most queries filter by org + project + time window
CREATE INDEX IX_WorkItemEvents_OrgId_ProjectId_ChangedAt_DESC
    ON WorkItemEvents (OrgId, ProjectId, ChangedAt DESC);

-- Deduplication: quickly check if a work item's transition was already ingested
CREATE INDEX IX_WorkItemEvents_OrgId_WorkItemId
    ON WorkItemEvents (OrgId, WorkItemId);
