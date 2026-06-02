using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.SQL.Migrations
{
    /// <inheritdoc />
    public partial class DoraMetricsComputedDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ComputedDate",
                table: "DoraMetrics",
                type: "date",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Backfill ComputedDate from ComputedAt for any pre-existing rows so the
            // subsequent UNIQUE index doesn't blow up on duplicate 0001-01-01 buckets.
            // SQL Server-only; safe no-op on InMemory because migrations are not applied there.
            migrationBuilder.Sql(
                "UPDATE DoraMetrics SET ComputedDate = CAST(ComputedAt AT TIME ZONE 'UTC' AS date);");

            // Collapse any prior duplicate per-day rows by keeping the most recent and
            // deleting older ones. Without this, the UNIQUE index creation below fails
            // for any customer that already had multiple per-day rows from before this
            // migration shipped.
            migrationBuilder.Sql(@"
                WITH ranked AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (
                               PARTITION BY OrgId, ProjectId, ComputedDate
                               ORDER BY ComputedAt DESC
                           ) AS rn
                    FROM DoraMetrics
                )
                DELETE FROM DoraMetrics
                WHERE Id IN (SELECT Id FROM ranked WHERE rn > 1);");

            migrationBuilder.CreateIndex(
                name: "UX_DoraMetrics_OrgId_ProjectId_ComputedDate",
                table: "DoraMetrics",
                columns: new[] { "OrgId", "ProjectId", "ComputedDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_DoraMetrics_OrgId_ProjectId_ComputedDate",
                table: "DoraMetrics");

            migrationBuilder.DropColumn(
                name: "ComputedDate",
                table: "DoraMetrics");
        }
    }
}
