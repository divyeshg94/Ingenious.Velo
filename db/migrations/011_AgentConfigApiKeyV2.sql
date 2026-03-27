-- Migration 011: Replace service principal columns with a single ApiKey column
-- Drops TenantId, ClientId, and ClientSecret from AgentConfigurations and adds ApiKey.
-- The value is encrypted at application level via ASP.NET Core Data Protection before storage.

IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_NAME = 'AgentConfigurations' AND COLUMN_NAME = 'TenantId')
BEGIN
    ALTER TABLE AgentConfigurations DROP COLUMN TenantId;
END
GO

IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_NAME = 'AgentConfigurations' AND COLUMN_NAME = 'ClientId')
BEGIN
    ALTER TABLE AgentConfigurations DROP COLUMN ClientId;
END
GO

IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_NAME = 'AgentConfigurations' AND COLUMN_NAME = 'ClientSecret')
BEGIN
    ALTER TABLE AgentConfigurations DROP COLUMN ClientSecret;
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'AgentConfigurations' AND COLUMN_NAME = 'ApiKey')
BEGIN
    ALTER TABLE AgentConfigurations
        ADD ApiKey NVARCHAR(1000) NULL;
END
GO
