using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.SQL.Migrations
{
    /// <inheritdoc />
    public partial class AddGrowthFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdminContactEmail",
                table: "Organizations",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MarketingOptOut",
                table: "Organizations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReEngagementEmailSentAt",
                table: "Organizations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstDoraViewedAt",
                table: "Organizations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstShareCreatedAt",
                table: "Organizations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SharedReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OrgDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProjectId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RepositoryName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MetricsSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SharedReports_OrgId_CreatedAt_DESC",
                table: "SharedReports",
                columns: new[] { "OrgId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SharedReports_ExpiresAt",
                table: "SharedReports",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SharedReports");

            migrationBuilder.DropColumn(
                name: "AdminContactEmail",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "MarketingOptOut",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ReEngagementEmailSentAt",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "FirstDoraViewedAt",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "FirstShareCreatedAt",
                table: "Organizations");
        }
    }
}
