using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.SQL.Migrations
{
    /// <inheritdoc />
    public partial class PipelineRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "PullRequestEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProjectId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PrId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceBranch = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TargetBranch = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    ReviewerCount = table.Column<int>(type: "int", nullable: false),
                    IngestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestEvents", x => x.Id);
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
                name: "IX_PullRequestEvents_OrgId_PrId_Status",
                table: "PullRequestEvents",
                columns: new[] { "OrgId", "PrId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestEvents_OrgId_ProjectId_CreatedAt_DESC",
                table: "PullRequestEvents",
                columns: new[] { "OrgId", "ProjectId", "CreatedAt" },
                descending: new[] { false, false, true });

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
                name: "PullRequestEvents");

            migrationBuilder.DropTable(
                name: "TeamMappings");

            migrationBuilder.DropIndex(
                name: "IX_PipelineRuns_OrgId_ProjectId_RepositoryName",
                table: "PipelineRuns");

            migrationBuilder.DropColumn(
                name: "RepositoryName",
                table: "PipelineRuns");
        }
    }
}
