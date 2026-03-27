-- Migration 010: Replace ApiKey column with service principal credential columns on AgentConfigurations.
-- Reason: PersistentAgentsClient (GA) only accepts TokenCredential — AzureKeyCredential is not supported.
-- Cross-tenant Foundry access now uses ClientSecretCredential(tenantId, clientId, clientSecret).
-- ClientSecret is still encrypted via ASP.NET Core Data Protection before storage.
-- Idempotent: each ALTER is guarded by a column-existence check.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'AgentConfigurations') AND name = N'TenantId'
)
BEGIN
    ALTER TABLE [AgentConfigurations]
        ADD [TenantId] NVARCHAR(200) NULL;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'AgentConfigurations') AND name = N'ClientId'
)
BEGIN
    ALTER TABLE [AgentConfigurations]
        ADD [ClientId] NVARCHAR(200) NULL;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'AgentConfigurations') AND name = N'ClientSecret'
)
BEGIN
    ALTER TABLE [AgentConfigurations]
        ADD [ClientSecret] NVARCHAR(1000) NULL;
END;

-- Drop the old ApiKey column if it still exists (introduced in migration 009).
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'AgentConfigurations') AND name = N'ApiKey'
)
BEGIN
    ALTER TABLE [AgentConfigurations]
        DROP COLUMN [ApiKey];
END;
