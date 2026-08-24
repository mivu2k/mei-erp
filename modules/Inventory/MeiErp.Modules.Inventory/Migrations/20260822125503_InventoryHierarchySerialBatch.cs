using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Inventory.Migrations
{
    /// <inheritdoc />
    public partial class InventoryHierarchySerialBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                schema: "inventory",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBatchTracked",
                schema: "inventory",
                table: "Items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSerialized",
                schema: "inventory",
                table: "Items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                schema: "inventory",
                table: "Items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ParentItemId",
                schema: "inventory",
                table: "Items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductFamilyId",
                schema: "inventory",
                table: "Items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReorderQuantity",
                schema: "inventory",
                table: "Items",
                type: "numeric(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ProductFamilies",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SkuPrefix = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_ProductFamilies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockBatches",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    BatchNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    RemainingQuantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Reference = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_StockBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockBatches_Items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "inventory",
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockBatches_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalSchema: "inventory",
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockUnits",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StockBatchId = table.Column<int>(type: "integer", nullable: true),
                    WarehouseId = table.Column<int>(type: "integer", nullable: true),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IssuedTo = table.Column<string>(type: "text", nullable: true),
                    Reference = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_StockUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockUnits_Items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "inventory",
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockUnits_StockBatches_StockBatchId",
                        column: x => x.StockBatchId,
                        principalSchema: "inventory",
                        principalTable: "StockBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StockUnits_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalSchema: "inventory",
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Items_Barcode",
                schema: "inventory",
                table: "Items",
                column: "Barcode",
                unique: true,
                filter: "\"Barcode\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Items_ParentItemId",
                schema: "inventory",
                table: "Items",
                column: "ParentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_ProductFamilyId",
                schema: "inventory",
                table: "Items",
                column: "ProductFamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductFamilies_IsDeleted",
                schema: "inventory",
                table: "ProductFamilies",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ProductFamilies_Name",
                schema: "inventory",
                table: "ProductFamilies",
                column: "Name",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_IsDeleted",
                schema: "inventory",
                table: "StockBatches",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_ItemId_BatchNumber",
                schema: "inventory",
                table: "StockBatches",
                columns: new[] { "ItemId", "BatchNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_WarehouseId",
                schema: "inventory",
                table: "StockBatches",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockUnits_IsDeleted",
                schema: "inventory",
                table: "StockUnits",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_StockUnits_ItemId_SerialNumber",
                schema: "inventory",
                table: "StockUnits",
                columns: new[] { "ItemId", "SerialNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StockUnits_StockBatchId",
                schema: "inventory",
                table: "StockUnits",
                column: "StockBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockUnits_WarehouseId",
                schema: "inventory",
                table: "StockUnits",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Items_ParentItemId",
                schema: "inventory",
                table: "Items",
                column: "ParentItemId",
                principalSchema: "inventory",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_ProductFamilies_ProductFamilyId",
                schema: "inventory",
                table: "Items",
                column: "ProductFamilyId",
                principalSchema: "inventory",
                principalTable: "ProductFamilies",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_Items_ParentItemId",
                schema: "inventory",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_ProductFamilies_ProductFamilyId",
                schema: "inventory",
                table: "Items");

            migrationBuilder.DropTable(
                name: "ProductFamilies",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "StockUnits",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "StockBatches",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "IX_Items_Barcode",
                schema: "inventory",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_ParentItemId",
                schema: "inventory",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_ProductFamilyId",
                schema: "inventory",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Barcode",
                schema: "inventory",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "IsBatchTracked",
                schema: "inventory",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "IsSerialized",
                schema: "inventory",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "inventory",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "ParentItemId",
                schema: "inventory",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "ProductFamilyId",
                schema: "inventory",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "ReorderQuantity",
                schema: "inventory",
                table: "Items");
        }
    }
}
