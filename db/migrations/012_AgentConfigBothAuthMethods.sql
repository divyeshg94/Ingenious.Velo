-- Migration 012: Support both API key AND service principal auth on AgentConfigurations.
-- ApiKey was added in 011. This migration adds back TenantId/ClientId/ClientSecret
-- so admins can choose either method (or neither — Velo Managed Identity is the default).
-- All credential columns are encrypted at application level before storage.

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'AgentConfigurations' AND COLUMN_NAME = 'TenantId')
BEGIN
    ALTER TABLE AgentConfigurations
        ADD TenantId NVARCHAR(200) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'AgentConfigurations' AND COLUMN_NAME = 'ClientId')
BEGIN
    ALTER TABLE AgentConfigurations
        ADD ClientId NVARCHAR(200) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'AgentConfigurations' AND COLUMN_NAME = 'ClientSecret')
BEGIN
    ALTER TABLE AgentConfigurations
        ADD ClientSecret NVARCHAR(1000) NULL;
END
GO
