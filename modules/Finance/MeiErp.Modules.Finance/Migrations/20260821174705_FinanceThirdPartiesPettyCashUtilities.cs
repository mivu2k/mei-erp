using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class FinanceThirdPartiesPettyCashUtilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PettyCashBoxes",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CustodianName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustodianUserId = table.Column<string>(type: "text", nullable: true),
                    Float = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    AccountId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_PettyCashBoxes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PettyCashBoxes_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "finance",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ThirdParties",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Side = table.Column<int>(type: "integer", nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Cnic = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AccountId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_ThirdParties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThirdParties_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "finance",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UtilityConnections",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ConnectionNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Provider = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ExpenseAccountId = table.Column<int>(type: "integer", nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_UtilityConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UtilityConnections_Accounts_ExpenseAccountId",
                        column: x => x.ExpenseAccountId,
                        principalSchema: "finance",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PettyCashEntries",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BoxId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ExpenseAccountId = table.Column<int>(type: "integer", nullable: true),
                    PaidTo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReceiptNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    VoucherId = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_PettyCashEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PettyCashEntries_Accounts_ExpenseAccountId",
                        column: x => x.ExpenseAccountId,
                        principalSchema: "finance",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PettyCashEntries_PettyCashBoxes_BoxId",
                        column: x => x.BoxId,
                        principalSchema: "finance",
                        principalTable: "PettyCashBoxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UtilityBills",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    ConnectionId = table.Column<int>(type: "integer", nullable: false),
                    BillingMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    BillNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Units = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: true),
                    VoucherId = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_UtilityBills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UtilityBills_UtilityConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "finance",
                        principalTable: "UtilityConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PettyCashBoxes_AccountId",
                schema: "finance",
                table: "PettyCashBoxes",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PettyCashBoxes_IsDeleted",
                schema: "finance",
                table: "PettyCashBoxes",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_PettyCashEntries_BoxId_Date",
                schema: "finance",
                table: "PettyCashEntries",
                columns: new[] { "BoxId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_PettyCashEntries_ExpenseAccountId",
                schema: "finance",
                table: "PettyCashEntries",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PettyCashEntries_IsDeleted",
                schema: "finance",
                table: "PettyCashEntries",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ThirdParties_AccountId",
                schema: "finance",
                table: "ThirdParties",
                column: "AccountId",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ThirdParties_IsDeleted",
                schema: "finance",
                table: "ThirdParties",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ThirdParties_Name",
                schema: "finance",
                table: "ThirdParties",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_ConnectionId_BillingMonth",
                schema: "finance",
                table: "UtilityBills",
                columns: new[] { "ConnectionId", "BillingMonth" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_IsDeleted",
                schema: "finance",
                table: "UtilityBills",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityBills_PaidOn",
                schema: "finance",
                table: "UtilityBills",
                column: "PaidOn");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityConnections_ExpenseAccountId",
                schema: "finance",
                table: "UtilityConnections",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityConnections_IsDeleted",
                schema: "finance",
                table: "UtilityConnections",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PettyCashEntries",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "ThirdParties",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "UtilityBills",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "PettyCashBoxes",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "UtilityConnections",
                schema: "finance");
        }
    }
}
