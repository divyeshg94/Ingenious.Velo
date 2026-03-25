using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.SQL.Migrations;

/// <inheritdoc />
public partial class AddRepositoryNameAndTeamMappings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add RepositoryName column to PipelineRuns (nullable — backfilled on next sync)
        migrationBuilder.AddColumn<string>(
            name: "RepositoryName",
            table: "PipelineRuns",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_PipelineRuns_OrgId_ProjectId_RepositoryName",
            table: "PipelineRuns",
            columns: new[] { "OrgId", "ProjectId", "RepositoryName" });

        // Create TeamMappings table
        migrationBuilder.CreateTable(
            name: "TeamMappings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
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
            name: "IX_TeamMappings_OrgId_ProjectId",
            table: "TeamMappings",
            columns: new[] { "OrgId", "ProjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_TeamMappings_OrgId_ProjectId_RepositoryName",
            table: "TeamMappings",
            columns: new[] { "OrgId", "ProjectId", "RepositoryName" },
            unique: true,
            filter: "[IsDeleted] = 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TeamMappings");
        migrationBuilder.DropIndex(name: "IX_PipelineRuns_OrgId_ProjectId_RepositoryName", table: "PipelineRuns");
        migrationBuilder.DropColumn(name: "RepositoryName", table: "PipelineRuns");
    }
}
