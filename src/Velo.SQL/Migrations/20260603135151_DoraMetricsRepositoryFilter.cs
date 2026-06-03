using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.SQL.Migrations
{
    /// <inheritdoc />
    public partial class DoraMetricsRepositoryFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_DoraMetrics_OrgId_ProjectId_ComputedDate",
                table: "DoraMetrics");

            migrationBuilder.AddColumn<string>(
                name: "RepositoryName",
                table: "TeamHealth",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RepositoryName",
                table: "DoraMetrics",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_TeamHealth_OrgId_ProjectId_RepositoryName_ComputedAt_DESC",
                table: "TeamHealth",
                columns: new[] { "OrgId", "ProjectId", "RepositoryName", "ComputedAt" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_DoraMetrics_OrgId_ProjectId_RepositoryName_ComputedAt_DESC",
                table: "DoraMetrics",
                columns: new[] { "OrgId", "ProjectId", "RepositoryName", "ComputedAt" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "UX_DoraMetrics_OrgId_ProjectId_ComputedDate_RepositoryName",
                table: "DoraMetrics",
                columns: new[] { "OrgId", "ProjectId", "ComputedDate", "RepositoryName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeamHealth_OrgId_ProjectId_RepositoryName_ComputedAt_DESC",
                table: "TeamHealth");

            migrationBuilder.DropIndex(
                name: "IX_DoraMetrics_OrgId_ProjectId_RepositoryName_ComputedAt_DESC",
                table: "DoraMetrics");

            migrationBuilder.DropIndex(
                name: "UX_DoraMetrics_OrgId_ProjectId_ComputedDate_RepositoryName",
                table: "DoraMetrics");

            migrationBuilder.DropColumn(
                name: "RepositoryName",
                table: "TeamHealth");

            migrationBuilder.DropColumn(
                name: "RepositoryName",
                table: "DoraMetrics");

            migrationBuilder.CreateIndex(
                name: "UX_DoraMetrics_OrgId_ProjectId_ComputedDate",
                table: "DoraMetrics",
                columns: new[] { "OrgId", "ProjectId", "ComputedDate" },
                unique: true);
        }
    }
}
