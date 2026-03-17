using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.SQL.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexesForPerformance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TeamHealth_ComputedAt_DESC",
                table: "TeamHealth",
                column: "ComputedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_TeamHealth_OrgId_ProjectId_ComputedAt_DESC",
                table: "TeamHealth",
                columns: new[] { "OrgId", "ProjectId", "ComputedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRuns_OrgId_IsDeployment",
                table: "PipelineRuns",
                columns: new[] { "OrgId", "IsDeployment" });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRuns_OrgId_ProjectId_StartTime_DESC",
                table: "PipelineRuns",
                columns: new[] { "OrgId", "ProjectId", "StartTime" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRuns_StartTime_DESC",
                table: "PipelineRuns",
                column: "StartTime",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_DoraMetrics_ComputedAt_DESC",
                table: "DoraMetrics",
                column: "ComputedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_DoraMetrics_OrgId_ProjectId_ComputedAt_DESC",
                table: "DoraMetrics",
                columns: new[] { "OrgId", "ProjectId", "ComputedAt" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeamHealth_ComputedAt_DESC",
                table: "TeamHealth");

            migrationBuilder.DropIndex(
                name: "IX_TeamHealth_OrgId_ProjectId_ComputedAt_DESC",
                table: "TeamHealth");

            migrationBuilder.DropIndex(
                name: "IX_PipelineRuns_OrgId_IsDeployment",
                table: "PipelineRuns");

            migrationBuilder.DropIndex(
                name: "IX_PipelineRuns_OrgId_ProjectId_StartTime_DESC",
                table: "PipelineRuns");

            migrationBuilder.DropIndex(
                name: "IX_PipelineRuns_StartTime_DESC",
                table: "PipelineRuns");

            migrationBuilder.DropIndex(
                name: "IX_DoraMetrics_ComputedAt_DESC",
                table: "DoraMetrics");

            migrationBuilder.DropIndex(
                name: "IX_DoraMetrics_OrgId_ProjectId_ComputedAt_DESC",
                table: "DoraMetrics");
        }
    }
}
