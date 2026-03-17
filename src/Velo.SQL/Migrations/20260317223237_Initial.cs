using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.SQL.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DoraMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProjectId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeploymentFrequency = table.Column<double>(type: "float", nullable: false),
                    DeploymentFrequencyRating = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LeadTimeForChangesHours = table.Column<double>(type: "float", nullable: false),
                    LeadTimeRating = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangeFailureRate = table.Column<double>(type: "float", nullable: false),
                    ChangeFailureRating = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MeanTimeToRestoreHours = table.Column<double>(type: "float", nullable: false),
                    MttrRating = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReworkRate = table.Column<double>(type: "float", nullable: false),
                    ReworkRateRating = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoraMetrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    OrgId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OrgUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsPremium = table.Column<bool>(type: "bit", nullable: false),
                    DailyTokenBudget = table.Column<int>(type: "int", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.OrgId);
                });

            migrationBuilder.CreateTable(
                name: "PipelineRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProjectId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AdoPipelineId = table.Column<int>(type: "int", nullable: false),
                    PipelineName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RunNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    IsDeployment = table.Column<bool>(type: "bit", nullable: false),
                    StageName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TriggeredBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IngestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamHealth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProjectId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CodingTimeHours = table.Column<double>(type: "float", nullable: false),
                    ReviewTimeHours = table.Column<double>(type: "float", nullable: false),
                    MergeTimeHours = table.Column<double>(type: "float", nullable: false),
                    DeployTimeHours = table.Column<double>(type: "float", nullable: false),
                    AveragePrSizeLines = table.Column<double>(type: "float", nullable: false),
                    PrCommentDensity = table.Column<double>(type: "float", nullable: false),
                    PrApprovalRate = table.Column<double>(type: "float", nullable: false),
                    TestPassRate = table.Column<double>(type: "float", nullable: false),
                    FlakyTestRate = table.Column<double>(type: "float", nullable: false),
                    DeploymentRiskScore = table.Column<double>(type: "float", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamHealth", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoraMetrics");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropTable(
                name: "PipelineRuns");

            migrationBuilder.DropTable(
                name: "TeamHealth");
        }
    }
}
