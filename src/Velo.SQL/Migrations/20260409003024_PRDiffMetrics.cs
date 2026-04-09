using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.SQL.Migrations
{
    /// <inheritdoc />
    public partial class PRDiffMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovedCount",
                table: "PullRequestEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CycleDurationMinutes",
                table: "PullRequestEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FilesChanged",
                table: "PullRequestEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstApprovedAt",
                table: "PullRequestEvents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LinesAdded",
                table: "PullRequestEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LinesDeleted",
                table: "PullRequestEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RejectedCount",
                table: "PullRequestEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerNames",
                table: "PullRequestEvents",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedCount",
                table: "PullRequestEvents");

            migrationBuilder.DropColumn(
                name: "CycleDurationMinutes",
                table: "PullRequestEvents");

            migrationBuilder.DropColumn(
                name: "FilesChanged",
                table: "PullRequestEvents");

            migrationBuilder.DropColumn(
                name: "FirstApprovedAt",
                table: "PullRequestEvents");

            migrationBuilder.DropColumn(
                name: "LinesAdded",
                table: "PullRequestEvents");

            migrationBuilder.DropColumn(
                name: "LinesDeleted",
                table: "PullRequestEvents");

            migrationBuilder.DropColumn(
                name: "RejectedCount",
                table: "PullRequestEvents");

            migrationBuilder.DropColumn(
                name: "ReviewerNames",
                table: "PullRequestEvents");
        }
    }
}
