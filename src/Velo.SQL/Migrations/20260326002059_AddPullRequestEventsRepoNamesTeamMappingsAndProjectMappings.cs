using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.SQL.Migrations
{
    /// <inheritdoc />
    public partial class AddPullRequestEventsRepoNamesTeamMappingsAndProjectMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PullRequestEvents was introduced by a hand-written migration that may never have
            // been applied to the database.  Create it if absent; otherwise just rename the index.
            migrationBuilder.Sql(@"
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
");

            migrationBuilder.AddColumn<string>(
                name: "RepositoryName",
                table: "PipelineRuns",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProjectGuid = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProjectName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProjectId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RepositoryName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TeamName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRuns_OrgId_ProjectId_RepositoryName",
                table: "PipelineRuns",
                columns: new[] { "OrgId", "ProjectId", "RepositoryName" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMappings_OrgId_ProjectGuid",
                table: "ProjectMappings",
                columns: new[] { "OrgId", "ProjectGuid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMappings_OrgId_ProjectId",
                table: "TeamMappings",
                columns: new[] { "OrgId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMappings_OrgId_ProjectId_RepositoryName_Unique",
                table: "TeamMappings",
                columns: new[] { "OrgId", "ProjectId", "RepositoryName" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectMappings");

            migrationBuilder.DropTable(
                name: "TeamMappings");

            migrationBuilder.DropIndex(
                name: "IX_PipelineRuns_OrgId_ProjectId_RepositoryName",
                table: "PipelineRuns");

            migrationBuilder.DropColumn(
                name: "RepositoryName",
                table: "PipelineRuns");

            migrationBuilder.RenameIndex(
                name: "IX_PullRequestEvents_OrgId_ProjectId_CreatedAt_DESC",
                table: "PullRequestEvents",
                newName: "IX_PullRequestEvents_OrgId_ProjectId_CreatedAt");
        }
    }
}
