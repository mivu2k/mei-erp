using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Hr.Migrations
{
    /// <inheritdoc />
    public partial class Attendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                schema: "hr",
                table: "Holidays",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CardNumber",
                schema: "hr",
                table: "Employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QrSecret",
                schema: "hr",
                table: "Employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShiftId",
                schema: "hr",
                table: "Employees",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendanceDays",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    FirstIn = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    LastOut = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    PunchCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    WorkedMinutes = table.Column<int>(type: "integer", nullable: false),
                    LateMinutes = table.Column<int>(type: "integer", nullable: false),
                    EarlyLeaveMinutes = table.Column<int>(type: "integer", nullable: false),
                    OvertimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    OverriddenById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    OverriddenByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OverriddenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OverrideReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LeaveRequestId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceDays_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceDays_LeaveRequests_LeaveRequestId",
                        column: x => x.LeaveRequestId,
                        principalSchema: "hr",
                        principalTable: "LeaveRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceStations",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Location = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastPunchAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastPunchDescription = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceStations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shifts",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartsAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndsAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    GraceMinutes = table.Column<int>(type: "integer", nullable: false),
                    HalfDayMinutes = table.Column<int>(type: "integer", nullable: false),
                    MinimumMinutes = table.Column<int>(type: "integer", nullable: false),
                    OvertimeAfterMinutes = table.Column<int>(type: "integer", nullable: false),
                    BreakMinutes = table.Column<int>(type: "integer", nullable: false),
                    WeeklyOffMask = table.Column<int>(type: "integer", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shifts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendancePunches",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AttendanceStationId = table.Column<int>(type: "integer", nullable: true),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    PunchedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Evidence = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendancePunches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendancePunches_AttendanceStations_AttendanceStationId",
                        column: x => x.AttendanceStationId,
                        principalSchema: "hr",
                        principalTable: "AttendanceStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AttendancePunches_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "hr",
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CardNumber",
                schema: "hr",
                table: "Employees",
                column: "CardNumber",
                unique: true,
                filter: "\"CardNumber\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_ShiftId",
                schema: "hr",
                table: "Employees",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDays_Date",
                schema: "hr",
                table: "AttendanceDays",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDays_EmployeeId_Date",
                schema: "hr",
                table: "AttendanceDays",
                columns: new[] { "EmployeeId", "Date" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDays_IsDeleted",
                schema: "hr",
                table: "AttendanceDays",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceDays_LeaveRequestId",
                schema: "hr",
                table: "AttendanceDays",
                column: "LeaveRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePunches_AttendanceStationId",
                schema: "hr",
                table: "AttendancePunches",
                column: "AttendanceStationId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePunches_EmployeeId_PunchedAt",
                schema: "hr",
                table: "AttendancePunches",
                columns: new[] { "EmployeeId", "PunchedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceStations_AccessToken",
                schema: "hr",
                table: "AttendanceStations",
                column: "AccessToken",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceStations_IsDeleted",
                schema: "hr",
                table: "AttendanceStations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_IsDeleted",
                schema: "hr",
                table: "Shifts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_Name",
                schema: "hr",
                table: "Shifts",
                column: "Name",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Shifts_ShiftId",
                schema: "hr",
                table: "Employees",
                column: "ShiftId",
                principalSchema: "hr",
                principalTable: "Shifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Shifts_ShiftId",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropTable(
                name: "AttendanceDays",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "AttendancePunches",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "Shifts",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "AttendanceStations",
                schema: "hr");

            migrationBuilder.DropIndex(
                name: "IX_Employees_CardNumber",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Employees_ShiftId",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "IsPaid",
                schema: "hr",
                table: "Holidays");

            migrationBuilder.DropColumn(
                name: "CardNumber",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "QrSecret",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "ShiftId",
                schema: "hr",
                table: "Employees");
        }
    }
}
