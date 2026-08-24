using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class PaymentRequestLineParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentRequestLines",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PaymentRequestId = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExpenseAccountId = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_PaymentRequestLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRequestLines_Accounts_ExpenseAccountId",
                        column: x => x.ExpenseAccountId,
                        principalSchema: "finance",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentRequestLines_PaymentRequests_PaymentRequestId",
                        column: x => x.PaymentRequestId,
                        principalSchema: "finance",
                        principalTable: "PaymentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequestLines_ExpenseAccountId",
                schema: "finance",
                table: "PaymentRequestLines",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequestLines_IsDeleted",
                schema: "finance",
                table: "PaymentRequestLines",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequestLines_PaymentRequestId",
                schema: "finance",
                table: "PaymentRequestLines",
                column: "PaymentRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentRequestLines",
                schema: "finance");
        }
    }
}
