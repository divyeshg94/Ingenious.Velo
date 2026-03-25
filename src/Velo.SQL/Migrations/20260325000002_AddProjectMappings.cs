using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.SQL.Migrations;

/// <inheritdoc />
public partial class AddProjectMappings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProjectMappings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                OrgId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ProjectGuid = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ProjectName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectMappings", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectMappings_OrgId_ProjectGuid",
            table: "ProjectMappings",
            columns: new[] { "OrgId", "ProjectGuid" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProjectMappings");
    }
}
