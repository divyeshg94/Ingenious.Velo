-- ============================================================
-- Migration 009: Add ApiKey column to AgentConfigurations
-- Stores the ASP.NET Core Data Protection-encrypted API key.
-- Null = org uses Velo Managed Identity instead of an API key.
-- Idempotent — safe to run multiple times.
-- ============================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('AgentConfigurations') AND name = 'ApiKey'
)
BEGIN
    ALTER TABLE AgentConfigurations
        ADD ApiKey NVARCHAR(1000) NULL;

    PRINT 'AgentConfigurations.ApiKey column added.';
END
ELSE
BEGIN
    PRINT 'AgentConfigurations.ApiKey already exists — skipped.';
END
GO
