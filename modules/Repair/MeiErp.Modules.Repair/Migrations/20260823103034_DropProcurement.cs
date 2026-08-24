using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Repair.Migrations
{
    /// <inheritdoc />
    public partial class DropProcurement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepairPurchaseLines",
                schema: "repair");

            migrationBuilder.DropTable(
                name: "RepairParts",
                schema: "repair");

            migrationBuilder.DropTable(
                name: "RepairPurchases",
                schema: "repair");

            migrationBuilder.DropTable(
                name: "RepairSuppliers",
                schema: "repair");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RepairSuppliers",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Address = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedBy = table.Column<string>(type: "text", nullable: true),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    TaxNumber = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepairSuppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RepairParts",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LastSupplierId = table.Column<int>(type: "integer", nullable: true),
                    AverageCost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedBy = table.Column<string>(type: "text", nullable: true),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    LastPurchaseCost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    LastPurchasedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PurchasedQuantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    SellingPrice = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepairParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepairParts_RepairSuppliers_LastSupplierId",
                        column: x => x.LastSupplierId,
                        principalSchema: "repair",
                        principalTable: "RepairSuppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RepairPurchases",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupplierId = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedBy = table.Column<string>(type: "text", nullable: true),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OtherCharges = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    PurchasedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceivedById = table.Column<string>(type: "text", nullable: false),
                    ReceivedByName = table.Column<string>(type: "text", nullable: false),
                    SupplierInvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepairPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepairPurchases_RepairSuppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "repair",
                        principalTable: "RepairSuppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RepairPurchaseLines",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PartId = table.Column<int>(type: "integer", nullable: false),
                    RepairPurchaseId = table.Column<int>(type: "integer", nullable: false),
                    NewSellingPrice = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepairPurchaseLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepairPurchaseLines_RepairParts_PartId",
                        column: x => x.PartId,
                        principalSchema: "repair",
                        principalTable: "RepairParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RepairPurchaseLines_RepairPurchases_RepairPurchaseId",
                        column: x => x.RepairPurchaseId,
                        principalSchema: "repair",
                        principalTable: "RepairPurchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RepairParts_IsDeleted",
                schema: "repair",
                table: "RepairParts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RepairParts_LastSupplierId",
                schema: "repair",
                table: "RepairParts",
                column: "LastSupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_RepairParts_Sku",
                schema: "repair",
                table: "RepairParts",
                column: "Sku",
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Sku\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RepairPurchaseLines_PartId",
                schema: "repair",
                table: "RepairPurchaseLines",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_RepairPurchaseLines_RepairPurchaseId",
                schema: "repair",
                table: "RepairPurchaseLines",
                column: "RepairPurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_RepairPurchases_IsDeleted",
                schema: "repair",
                table: "RepairPurchases",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RepairPurchases_Number",
                schema: "repair",
                table: "RepairPurchases",
                column: "Number",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_RepairPurchases_SupplierId",
                schema: "repair",
                table: "RepairPurchases",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_RepairSuppliers_IsDeleted",
                schema: "repair",
                table: "RepairSuppliers",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RepairSuppliers_Name",
                schema: "repair",
                table: "RepairSuppliers",
                column: "Name");
        }
    }
}
