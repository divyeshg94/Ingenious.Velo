using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.SQL.Migrations
{
    /// <inheritdoc />
    public partial class SecurityConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AadTenantId",
                table: "Organizations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeploymentName",
                table: "AgentConfigurations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "WorkItemEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProjectId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WorkItemId = table.Column<int>(type: "int", nullable: false),
                    WorkItemType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OldState = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NewState = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IngestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_AadTenantId",
                table: "Organizations",
                column: "AadTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemEvents_OrgId_ProjectId_ChangedAt_DESC",
                table: "WorkItemEvents",
                columns: new[] { "OrgId", "ProjectId", "ChangedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemEvents_OrgId_WorkItemId",
                table: "WorkItemEvents",
                columns: new[] { "OrgId", "WorkItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkItemEvents");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_AadTenantId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "AadTenantId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "DeploymentName",
                table: "AgentConfigurations");
        }
    }
}
