using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Repair.Migrations
{
    /// <inheritdoc />
    public partial class RepairProcurement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RepairSuppliers",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    TaxNumber = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_RepairSuppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RepairParts",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SellingPrice = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    LastPurchaseCost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    AverageCost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    PurchasedQuantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    LastPurchasedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LastSupplierId = table.Column<int>(type: "integer", nullable: true),
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
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SupplierId = table.Column<int>(type: "integer", nullable: false),
                    SupplierInvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PurchasedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceivedById = table.Column<string>(type: "text", nullable: false),
                    ReceivedByName = table.Column<string>(type: "text", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    OtherCharges = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
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
                    RepairPurchaseId = table.Column<int>(type: "integer", nullable: false),
                    PartId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    NewSellingPrice = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
    }
}
