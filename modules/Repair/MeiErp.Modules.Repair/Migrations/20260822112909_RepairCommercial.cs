using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Repair.Migrations
{
    /// <inheritdoc />
    public partial class RepairCommercial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RepairQuotations",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    JobId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    TaxPercent = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CustomerApproved = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerApproved = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_RepairQuotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepairQuotations_Jobs_JobId",
                        column: x => x.JobId,
                        principalSchema: "repair",
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RepairOrders",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    QuotationId = table.Column<int>(type: "integer", nullable: false),
                    JobId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Tax = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    AmountPaid = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    PaymentStatus = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_RepairOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepairOrders_RepairQuotations_QuotationId",
                        column: x => x.QuotationId,
                        principalSchema: "repair",
                        principalTable: "RepairQuotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RepairQuotationLines",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RepairQuotationId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepairQuotationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepairQuotationLines_RepairQuotations_RepairQuotationId",
                        column: x => x.RepairQuotationId,
                        principalSchema: "repair",
                        principalTable: "RepairQuotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepairPayments",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RepairOrderId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_RepairPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepairPayments_RepairOrders_RepairOrderId",
                        column: x => x.RepairOrderId,
                        principalSchema: "repair",
                        principalTable: "RepairOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RepairOrders_IsDeleted",
                schema: "repair",
                table: "RepairOrders",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RepairOrders_Number",
                schema: "repair",
                table: "RepairOrders",
                column: "Number",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_RepairOrders_QuotationId",
                schema: "repair",
                table: "RepairOrders",
                column: "QuotationId",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_RepairPayments_IsDeleted",
                schema: "repair",
                table: "RepairPayments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RepairPayments_RepairOrderId",
                schema: "repair",
                table: "RepairPayments",
                column: "RepairOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RepairQuotationLines_RepairQuotationId",
                schema: "repair",
                table: "RepairQuotationLines",
                column: "RepairQuotationId");

            migrationBuilder.CreateIndex(
                name: "IX_RepairQuotations_IsDeleted",
                schema: "repair",
                table: "RepairQuotations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RepairQuotations_JobId",
                schema: "repair",
                table: "RepairQuotations",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_RepairQuotations_Number",
                schema: "repair",
                table: "RepairQuotations",
                column: "Number",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepairPayments",
                schema: "repair");

            migrationBuilder.DropTable(
                name: "RepairQuotationLines",
                schema: "repair");

            migrationBuilder.DropTable(
                name: "RepairOrders",
                schema: "repair");

            migrationBuilder.DropTable(
                name: "RepairQuotations",
                schema: "repair");
        }
    }
}
