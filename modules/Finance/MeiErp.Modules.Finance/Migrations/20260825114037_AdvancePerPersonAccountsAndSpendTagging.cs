using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class AdvancePerPersonAccountsAndSpendTagging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_ParentId",
                schema: "finance",
                table: "Accounts");

            migrationBuilder.AddColumn<string>(
                name: "DepartmentId",
                schema: "finance",
                table: "VoucherLines",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectId",
                schema: "finance",
                table: "VoucherLines",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectId",
                schema: "finance",
                table: "PaymentRequests",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectName",
                schema: "finance",
                table: "PaymentRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "Attachment",
                schema: "finance",
                table: "PaymentRequestLines",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentContentType",
                schema: "finance",
                table: "PaymentRequestLines",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentName",
                schema: "finance",
                table: "PaymentRequestLines",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AdvanceAccountId",
                schema: "finance",
                table: "Advances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonId",
                schema: "finance",
                table: "Accounts",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_DepartmentId",
                schema: "finance",
                table: "VoucherLines",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_ProjectId",
                schema: "finance",
                table: "VoucherLines",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_ProjectId",
                schema: "finance",
                table: "PaymentRequests",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Advances_AdvanceAccountId",
                schema: "finance",
                table: "Advances",
                column: "AdvanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ParentId_PersonId",
                schema: "finance",
                table: "Accounts",
                columns: new[] { "ParentId", "PersonId" },
                unique: true,
                filter: "\"PersonId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_Advances_Accounts_AdvanceAccountId",
                schema: "finance",
                table: "Advances",
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
                name: "FK_Advances_Accounts_AdvanceAccountId",
                schema: "finance",
                table: "Advances");

            migrationBuilder.DropIndex(
                name: "IX_VoucherLines_DepartmentId",
                schema: "finance",
                table: "VoucherLines");

            migrationBuilder.DropIndex(
                name: "IX_VoucherLines_ProjectId",
                schema: "finance",
                table: "VoucherLines");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRequests_ProjectId",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropIndex(
                name: "IX_Advances_AdvanceAccountId",
                schema: "finance",
                table: "Advances");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_ParentId_PersonId",
                schema: "finance",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                schema: "finance",
                table: "VoucherLines");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                schema: "finance",
                table: "VoucherLines");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "ProjectName",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "Attachment",
                schema: "finance",
                table: "PaymentRequestLines");

            migrationBuilder.DropColumn(
                name: "AttachmentContentType",
                schema: "finance",
                table: "PaymentRequestLines");

            migrationBuilder.DropColumn(
                name: "AttachmentName",
                schema: "finance",
                table: "PaymentRequestLines");

            migrationBuilder.DropColumn(
                name: "AdvanceAccountId",
                schema: "finance",
                table: "Advances");

            migrationBuilder.DropColumn(
                name: "PersonId",
                schema: "finance",
                table: "Accounts");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ParentId",
                schema: "finance",
                table: "Accounts",
                column: "ParentId");
        }
    }
}
