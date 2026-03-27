-- ============================================================
-- Migration 008: Add AgentConfigurations table
-- Stores per-org Azure AI Foundry endpoint + agent ID config
-- Idempotent — safe to run multiple times.
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AgentConfigurations')
BEGIN
    CREATE TABLE AgentConfigurations (
        Id               UNIQUEIDENTIFIER  NOT NULL  DEFAULT NEWID()         PRIMARY KEY,
        OrgId            NVARCHAR(100)     NOT NULL,
        FoundryEndpoint  NVARCHAR(500)     NOT NULL,
        AgentId          NVARCHAR(200)     NOT NULL,
        DisplayName      NVARCHAR(200)     NULL,
        IsEnabled        BIT               NOT NULL  DEFAULT 1,
        CreatedAt        DATETIMEOFFSET    NOT NULL  DEFAULT SYSUTCDATETIME(),
        UpdatedAt        DATETIMEOFFSET    NOT NULL  DEFAULT SYSUTCDATETIME(),

        CONSTRAINT UQ_AgentConfigurations_OrgId UNIQUE (OrgId)
    );

    CREATE UNIQUE INDEX IX_AgentConfigurations_OrgId
        ON AgentConfigurations (OrgId);

    PRINT 'AgentConfigurations table created.';
END
ELSE
BEGIN
    PRINT 'AgentConfigurations table already exists — skipped.';
END
GO
