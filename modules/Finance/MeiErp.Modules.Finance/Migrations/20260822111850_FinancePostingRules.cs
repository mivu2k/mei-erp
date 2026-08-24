using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class FinancePostingRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceIdempotencyKey",
                schema: "finance",
                table: "Vouchers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PostingRules",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DebitAccountId = table.Column<int>(type: "integer", nullable: false),
                    CreditAccountId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_PostingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostingRules_Accounts_CreditAccountId",
                        column: x => x.CreditAccountId,
                        principalSchema: "finance",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PostingRules_Accounts_DebitAccountId",
                        column: x => x.DebitAccountId,
                        principalSchema: "finance",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_SourceModule_SourceDocumentType_SourceIdempotencyK~",
                schema: "finance",
                table: "Vouchers",
                columns: new[] { "SourceModule", "SourceDocumentType", "SourceIdempotencyKey" },
                unique: true,
                filter: "\"SourceIdempotencyKey\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_PostingRules_CreditAccountId",
                schema: "finance",
                table: "PostingRules",
                column: "CreditAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PostingRules_DebitAccountId",
                schema: "finance",
                table: "PostingRules",
                column: "DebitAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PostingRules_EventType",
                schema: "finance",
                table: "PostingRules",
                column: "EventType",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_PostingRules_IsDeleted",
                schema: "finance",
                table: "PostingRules",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PostingRules",
                schema: "finance");

            migrationBuilder.DropIndex(
                name: "IX_Vouchers_SourceModule_SourceDocumentType_SourceIdempotencyK~",
                schema: "finance",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "SourceIdempotencyKey",
                schema: "finance",
                table: "Vouchers");
        }
    }
}
