using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class MergeAdvancesIntoPaymentRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AdvanceExpenses_Advances_AdvanceId",
                schema: "finance",
                table: "AdvanceExpenses");

            migrationBuilder.AddColumn<int>(
                name: "AdvanceAccountId",
                schema: "finance",
                table: "PaymentRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ClearedDifference",
                schema: "finance",
                table: "PaymentRequests",
                type: "numeric(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "DifferenceHandling",
                schema: "finance",
                table: "PaymentRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DisbursedAmount",
                schema: "finance",
                table: "PaymentRequests",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisbursedUtc",
                schema: "finance",
                table: "PaymentRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisbursementVoucherId",
                schema: "finance",
                table: "PaymentRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "JustifiedAmount",
                schema: "finance",
                table: "PaymentRequests",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "JustifiedUtc",
                schema: "finance",
                table: "PaymentRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                schema: "finance",
                table: "PaymentRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SettledUtc",
                schema: "finance",
                table: "PaymentRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SettlementVoucherId",
                schema: "finance",
                table: "PaymentRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_AdvanceAccountId",
                schema: "finance",
                table: "PaymentRequests",
                column: "AdvanceAccountId");

            // ---- carry the existing advances across before the table goes ----
            //
            // EF scaffolds a bare DropTable here, which would take every live
            // advance with it. The rows are copied into PaymentRequests as
            // Kind=1 first, and their receipts repointed at the new ids.
            //
            // References can collide: director requests and director advances
            // both number themselves DFR-. Where one is already taken the
            // advance keeps its number with an -A suffix rather than failing
            // the unique index halfway through a deployment.
            migrationBuilder.Sql("""
                ALTER TABLE finance."PaymentRequests" ADD COLUMN "LegacyAdvanceId" integer;

                INSERT INTO finance."PaymentRequests" (
                    "Reference", "Title", "Description", "Kind", "Amount",
                    "RequestedByUserId", "RequestedByName", "DepartmentId",
                    "IsDirectorRequest", "NeededBy", "Status", "ApprovalRequestId",
                    "DecisionComment", "DisbursedAmount", "JustifiedAmount",
                    "AdvanceAccountId", "DisbursementVoucherId", "SettlementVoucherId",
                    "DifferenceHandling", "ClearedDifference",
                    "SubmittedUtc", "DisbursedUtc", "JustifiedUtc", "SettledUtc",
                    "CreatedUtc", "CreatedBy", "ModifiedUtc", "ModifiedBy",
                    "IsDeleted", "DeletedUtc", "DeletedBy", "LegacyAdvanceId")
                SELECT
                    CASE WHEN EXISTS (
                            SELECT 1 FROM finance."PaymentRequests" p
                            WHERE p."Reference" = a."Reference")
                         THEN a."Reference" || '-A'
                         ELSE a."Reference" END,
                    a."Purpose", NULL, 1, a."Amount",
                    a."PersonId", a."PersonName", a."DepartmentId",
                    a."IsDirectorRequest", a."NeededBy", a."Status", a."ApprovalRequestId",
                    a."DecisionComment", a."DisbursedAmount", a."JustifiedAmount",
                    a."AdvanceAccountId", a."DisbursementVoucherId", a."SettlementVoucherId",
                    a."DifferenceHandling", a."ClearedDifference",
                    a."SubmittedUtc", a."DisbursedUtc", a."JustifiedUtc", a."SettledUtc",
                    a."CreatedUtc", a."CreatedBy", a."ModifiedUtc", a."ModifiedBy",
                    a."IsDeleted", a."DeletedUtc", a."DeletedBy", a."Id"
                FROM finance."Advances" a;

                UPDATE finance."AdvanceExpenses" e
                SET "AdvanceId" = p."Id"
                FROM finance."PaymentRequests" p
                WHERE p."LegacyAdvanceId" = e."AdvanceId";

                ALTER TABLE finance."PaymentRequests" DROP COLUMN "LegacyAdvanceId";
                """);

            migrationBuilder.DropTable(
                name: "Advances",
                schema: "finance");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_Kind_Status",
                schema: "finance",
                table: "PaymentRequests",
                columns: new[] { "Kind", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_AdvanceExpenses_PaymentRequests_AdvanceId",
                schema: "finance",
                table: "AdvanceExpenses",
                column: "AdvanceId",
                principalSchema: "finance",
                principalTable: "PaymentRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentRequests_Accounts_AdvanceAccountId",
                schema: "finance",
                table: "PaymentRequests",
                column: "AdvanceAccountId",
                principalSchema: "finance",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AdvanceExpenses_PaymentRequests_AdvanceId",
                schema: "finance",
                table: "AdvanceExpenses");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentRequests_Accounts_AdvanceAccountId",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRequests_AdvanceAccountId",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRequests_Kind_Status",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "AdvanceAccountId",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "ClearedDifference",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "DifferenceHandling",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "DisbursedAmount",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "DisbursedUtc",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "DisbursementVoucherId",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "JustifiedAmount",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "JustifiedUtc",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "SettledUtc",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "SettlementVoucherId",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.CreateTable(
                name: "Advances",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdvanceAccountId = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ApprovalRequestId = table.Column<int>(type: "integer", nullable: true),
                    ClearedDifference = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecisionComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DepartmentId = table.Column<string>(type: "text", nullable: true),
                    DifferenceHandling = table.Column<int>(type: "integer", nullable: true),
                    DisbursedAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    DisbursedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisbursementVoucherId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    IsDirectorRequest = table.Column<bool>(type: "boolean", nullable: false),
                    JustifiedAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    JustifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NeededBy = table.Column<DateOnly>(type: "date", nullable: false),
                    PersonId = table.Column<string>(type: "text", nullable: false),
                    PersonName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SettledUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SettlementVoucherId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubmittedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Advances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Advances_Accounts_AdvanceAccountId",
                        column: x => x.AdvanceAccountId,
                        principalSchema: "finance",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Advances_AdvanceAccountId",
                schema: "finance",
                table: "Advances",
                column: "AdvanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Advances_IsDeleted",
                schema: "finance",
                table: "Advances",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Advances_IsDirectorRequest",
                schema: "finance",
                table: "Advances",
                column: "IsDirectorRequest");

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

            migrationBuilder.AddForeignKey(
                name: "FK_AdvanceExpenses_Advances_AdvanceId",
                schema: "finance",
                table: "AdvanceExpenses",
                column: "AdvanceId",
                principalSchema: "finance",
                principalTable: "Advances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
