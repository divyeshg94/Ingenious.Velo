-- Migration 013: Add DeploymentName column to AgentConfigurations.
-- Stores the Azure OpenAI model deployment name (e.g. gpt-4o) used when
-- Velo auto-creates the Foundry agent on first chat. Defaults to 'gpt-4o'.

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
               WHERE TABLE_NAME = 'AgentConfigurations' AND COLUMN_NAME = 'DeploymentName')
BEGIN
    ALTER TABLE AgentConfigurations
        ADD DeploymentName NVARCHAR(100) NOT NULL CONSTRAINT DF_AgentConfigurations_DeploymentName DEFAULT 'gpt-4o';
END
GO
