using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class SalaryAdvances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmployeeAdvanceId",
                schema: "finance",
                table: "PayslipLines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployeeAdvances",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PersonId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    PersonName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DepartmentId = table.Column<string>(type: "text", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InstallmentCount = table.Column<int>(type: "integer", nullable: false),
                    MonthlyDeduction = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    RepaidAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ApprovalRequestId = table.Column<int>(type: "integer", nullable: true),
                    DecisionComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AdvanceAccountId = table.Column<int>(type: "integer", nullable: true),
                    DisbursementVoucherId = table.Column<int>(type: "integer", nullable: true),
                    SubmittedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisbursedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SettledUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_EmployeeAdvances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeAdvances_Accounts_AdvanceAccountId",
                        column: x => x.AdvanceAccountId,
                        principalSchema: "finance",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeAdvanceInstallments",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeAdvanceId = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    PaidDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RepaymentVoucherId = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_EmployeeAdvanceInstallments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeAdvanceInstallments_EmployeeAdvances_EmployeeAdvanc~",
                        column: x => x.EmployeeAdvanceId,
                        principalSchema: "finance",
                        principalTable: "EmployeeAdvances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAdvanceInstallments_DueDate",
                schema: "finance",
                table: "EmployeeAdvanceInstallments",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAdvanceInstallments_EmployeeAdvanceId_Number",
                schema: "finance",
                table: "EmployeeAdvanceInstallments",
                columns: new[] { "EmployeeAdvanceId", "Number" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAdvanceInstallments_IsDeleted",
                schema: "finance",
                table: "EmployeeAdvanceInstallments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAdvances_AdvanceAccountId",
                schema: "finance",
                table: "EmployeeAdvances",
                column: "AdvanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAdvances_IsDeleted",
                schema: "finance",
                table: "EmployeeAdvances",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAdvances_PersonId_Status",
                schema: "finance",
                table: "EmployeeAdvances",
                columns: new[] { "PersonId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAdvances_Reference",
                schema: "finance",
                table: "EmployeeAdvances",
                column: "Reference",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAdvances_Status",
                schema: "finance",
                table: "EmployeeAdvances",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeAdvanceInstallments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "EmployeeAdvances",
                schema: "finance");

            migrationBuilder.DropColumn(
                name: "EmployeeAdvanceId",
                schema: "finance",
                table: "PayslipLines");
        }
    }
}
