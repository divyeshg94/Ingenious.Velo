using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.SQL.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationUserTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FirstAccessAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastAccessAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AccessCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationUsers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_OrgId_LastAccessAt_DESC",
                table: "ApplicationUsers",
                columns: new[] { "OrgId", "LastAccessAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_ApplicationUsers_OrgId_Email",
                table: "ApplicationUsers",
                columns: new[] { "OrgId", "Email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationUsers");
        }
    }
}
