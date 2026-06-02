using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.SQL.Migrations
{
    /// <inheritdoc />
    public partial class AddEstimationFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsChangeFailureRateEstimated",
                table: "DoraMetrics",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeploymentFrequencyEstimated",
                table: "DoraMetrics",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsLeadTimeApproximate",
                table: "DoraMetrics",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMttrEstimated",
                table: "DoraMetrics",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsReworkRateEstimated",
                table: "DoraMetrics",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsChangeFailureRateEstimated",
                table: "DoraMetrics");

            migrationBuilder.DropColumn(
                name: "IsDeploymentFrequencyEstimated",
                table: "DoraMetrics");

            migrationBuilder.DropColumn(
                name: "IsLeadTimeApproximate",
                table: "DoraMetrics");

            migrationBuilder.DropColumn(
                name: "IsMttrEstimated",
                table: "DoraMetrics");

            migrationBuilder.DropColumn(
                name: "IsReworkRateEstimated",
                table: "DoraMetrics");
        }
    }
}
