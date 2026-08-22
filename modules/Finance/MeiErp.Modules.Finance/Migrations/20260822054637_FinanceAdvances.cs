using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class FinanceAdvances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Advances",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PersonId = table.Column<string>(type: "text", nullable: false),
                    PersonName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DepartmentId = table.Column<string>(type: "text", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    DisbursedAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    JustifiedAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    NeededBy = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ApprovalRequestId = table.Column<int>(type: "integer", nullable: true),
                    DecisionComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DisbursementVoucherId = table.Column<int>(type: "integer", nullable: true),
                    SettlementVoucherId = table.Column<int>(type: "integer", nullable: true),
                    SubmittedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisbursedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    JustifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SettledUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DifferenceHandling = table.Column<int>(type: "integer", nullable: true),
                    ClearedDifference = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
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
                    table.PrimaryKey("PK_Advances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvanceExpenses",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdvanceId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ExpenseAccountId = table.Column<int>(type: "integer", nullable: true),
                    ReceiptNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvanceExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdvanceExpenses_Accounts_ExpenseAccountId",
                        column: x => x.ExpenseAccountId,
                        principalSchema: "finance",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdvanceExpenses_Advances_AdvanceId",
                        column: x => x.AdvanceId,
                        principalSchema: "finance",
                        principalTable: "Advances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceExpenses_AdvanceId",
                schema: "finance",
                table: "AdvanceExpenses",
                column: "AdvanceId");

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceExpenses_ExpenseAccountId",
                schema: "finance",
                table: "AdvanceExpenses",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Advances_IsDeleted",
                schema: "finance",
                table: "Advances",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Advances_PersonId_Status",
                schema: "finance",
                table: "Advances",
                columns: new[] { "PersonId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Advances_Reference",
                schema: "finance",
                table: "Advances",
                column: "Reference",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Advances_Status",
                schema: "finance",
                table: "Advances",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdvanceExpenses",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Advances",
                schema: "finance");
        }
    }
}
