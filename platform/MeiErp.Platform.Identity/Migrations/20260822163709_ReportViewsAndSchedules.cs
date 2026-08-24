using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Platform.Identity.Migrations
{
    /// <inheritdoc />
    public partial class ReportViewsAndSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportSchedules",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ReportKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    FiltersJson = table.Column<string>(type: "jsonb", nullable: false),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    RunAtLocal = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    DayOfMonth = table.Column<int>(type: "integer", nullable: false),
                    NextRunUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastRunUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRowCount = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SavedReportViews",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ReportKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    FiltersJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedReportViews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportSchedules_IsActive_NextRunUtc",
                schema: "platform",
                table: "ReportSchedules",
                columns: new[] { "IsActive", "NextRunUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportSchedules_UserId_ReportKey_Name",
                schema: "platform",
                table: "ReportSchedules",
                columns: new[] { "UserId", "ReportKey", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedReportViews_UserId_ReportKey_Name",
                schema: "platform",
                table: "SavedReportViews",
                columns: new[] { "UserId", "ReportKey", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportSchedules",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "SavedReportViews",
                schema: "platform");
        }
    }
}
