BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326002059_AddPullRequestEventsRepoNamesTeamMappingsAndProjectMappings'
)
BEGIN

    IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[PullRequestEvents]') AND type = 'U')
    BEGIN
        CREATE TABLE [PullRequestEvents] (
            [Id]            uniqueidentifier NOT NULL,
            [OrgId]         nvarchar(100)    NOT NULL,
            [ProjectId]     nvarchar(100)    NOT NULL,
            [PrId]          int              NOT NULL,
            [Title]         nvarchar(500)    NULL,
            [Status]        nvarchar(50)     NOT NULL,
            [SourceBranch]  nvarchar(500)    NULL,
            [TargetBranch]  nvarchar(500)    NULL,
            [CreatedAt]     datetimeoffset   NOT NULL,
            [ClosedAt]      datetimeoffset   NULL,
            [IsApproved]    bit              NOT NULL DEFAULT CAST(0 AS bit),
            [ReviewerCount] int              NOT NULL DEFAULT 0,
            [IngestedAt]    datetimeoffset   NOT NULL,
            [CreatedBy]     nvarchar(200)    NULL,
            [CreatedDate]   datetimeoffset   NOT NULL DEFAULT (SYSUTCDATETIME()),
            [ModifiedBy]    nvarchar(200)    NULL,
            [ModifiedDate]  datetimeoffset   NULL,
            [IsDeleted]     bit              NOT NULL DEFAULT CAST(0 AS bit),
            CONSTRAINT [PK_PullRequestEvents] PRIMARY KEY ([Id])
        );
        CREATE INDEX [IX_PullRequestEvents_OrgId_ProjectId_CreatedAt_DESC]
            ON [PullRequestEvents] ([OrgId], [ProjectId], [CreatedAt] DESC);
        CREATE INDEX [IX_PullRequestEvents_OrgId_PrId_Status]
            ON [PullRequestEvents] ([OrgId], [PrId], [Status]);
    END
    ELSE IF EXISTS (
        SELECT 1 FROM sys.indexes i
        JOIN  sys.objects  o ON i.object_id = o.object_id
        WHERE o.name = 'PullRequestEvents'
          AND i.name = 'IX_PullRequestEvents_OrgId_ProjectId_CreatedAt'
    )
    BEGIN
        EXEC sp_rename N'[PullRequestEvents].[IX_PullRequestEvents_OrgId_ProjectId_CreatedAt]',
                       N'IX_PullRequestEvents_OrgId_ProjectId_CreatedAt_DESC', 'INDEX';
    END;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326002059_AddPullRequestEventsRepoNamesTeamMappingsAndProjectMappings'
)
BEGIN
    ALTER TABLE [PipelineRuns] ADD [RepositoryName] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326002059_AddPullRequestEventsRepoNamesTeamMappingsAndProjectMappings'
)
BEGIN
    CREATE TABLE [ProjectMappings] (
        [Id] uniqueidentifier NOT NULL,
        [OrgId] nvarchar(100) NOT NULL,
        [ProjectGuid] nvarchar(100) NOT NULL,
        [ProjectName] nvarchar(200) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_ProjectMappings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326002059_AddPullRequestEventsRepoNamesTeamMappingsAndProjectMappings'
)
BEGIN
    CREATE TABLE [TeamMappings] (
        [Id] uniqueidentifier NOT NULL,
        [OrgId] nvarchar(100) NOT NULL,
        [ProjectId] nvarchar(100) NOT NULL,
        [RepositoryName] nvarchar(200) NOT NULL,
        [TeamName] nvarchar(200) NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [CreatedDate] datetimeoffset NOT NULL DEFAULT (SYSUTCDATETIME()),
        [ModifiedBy] nvarchar(200) NULL,
        [ModifiedDate] datetimeoffset NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_TeamMappings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326002059_AddPullRequestEventsRepoNamesTeamMappingsAndProjectMappings'
)
BEGIN
    CREATE INDEX [IX_PipelineRuns_OrgId_ProjectId_RepositoryName] ON [PipelineRuns] ([OrgId], [ProjectId], [RepositoryName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326002059_AddPullRequestEventsRepoNamesTeamMappingsAndProjectMappings'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProjectMappings_OrgId_ProjectGuid] ON [ProjectMappings] ([OrgId], [ProjectGuid]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326002059_AddPullRequestEventsRepoNamesTeamMappingsAndProjectMappings'
)
BEGIN
    CREATE INDEX [IX_TeamMappings_OrgId_ProjectId] ON [TeamMappings] ([OrgId], [ProjectId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326002059_AddPullRequestEventsRepoNamesTeamMappingsAndProjectMappings'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_TeamMappings_OrgId_ProjectId_RepositoryName_Unique] ON [TeamMappings] ([OrgId], [ProjectId], [RepositoryName]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260326002059_AddPullRequestEventsRepoNamesTeamMappingsAndProjectMappings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260326002059_AddPullRequestEventsRepoNamesTeamMappingsAndProjectMappings', N'9.0.14');
END;

COMMIT;
GO

