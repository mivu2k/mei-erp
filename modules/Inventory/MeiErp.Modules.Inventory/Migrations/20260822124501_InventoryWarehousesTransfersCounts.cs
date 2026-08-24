using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Inventory.Migrations
{
    /// <inheritdoc />
    public partial class InventoryWarehousesTransfersCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                schema: "inventory",
                table: "StockMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Warehouses",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryCounts",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CountedByName = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    PostedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_InventoryCounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryCounts_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalSchema: "inventory",
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransfers",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FromWarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ToWarehouseId = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    RaisedByName = table.Column<string>(type: "text", nullable: false),
                    DispatchedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DispatchedBy = table.Column<string>(type: "text", nullable: true),
                    ReceivedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedBy = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_StockTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Warehouses_FromWarehouseId",
                        column: x => x.FromWarehouseId,
                        principalSchema: "inventory",
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Warehouses_ToWarehouseId",
                        column: x => x.ToWarehouseId,
                        principalSchema: "inventory",
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WarehouseBalances",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehouseBalances_Items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "inventory",
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehouseBalances_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalSchema: "inventory",
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Existing installations predate locations. Preserve their on-hand
            // figures by placing every item in one explicit default warehouse.
            migrationBuilder.Sql("""
                INSERT INTO inventory."Warehouses"
                    ("Name", "Code", "IsActive", "IsDefault", "CreatedUtc", "IsDeleted")
                VALUES ('Main warehouse', 'MAIN', true, true, NOW(), false);

                INSERT INTO inventory."WarehouseBalances" ("WarehouseId", "ItemId", "Quantity")
                SELECT w."Id", i."Id", i."QuantityOnHand"
                FROM inventory."Items" i
                CROSS JOIN inventory."Warehouses" w
                WHERE w."Code" = 'MAIN' AND w."IsDeleted" = false;

                UPDATE inventory."StockMovements" m
                SET "WarehouseId" = w."Id"
                FROM inventory."Warehouses" w
                WHERE w."Code" = 'MAIN' AND w."IsDeleted" = false;
                """);

            migrationBuilder.CreateTable(
                name: "InventoryCountLines",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InventoryCountId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ItemCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ItemName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SystemQuantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCountLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryCountLines_InventoryCounts_InventoryCountId",
                        column: x => x.InventoryCountId,
                        principalSchema: "inventory",
                        principalTable: "InventoryCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryCountLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "inventory",
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransferLines",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StockTransferId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    ItemCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ItemName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransferLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "inventory",
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferLines_StockTransfers_StockTransferId",
                        column: x => x.StockTransferId,
                        principalSchema: "inventory",
                        principalTable: "StockTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_WarehouseId",
                schema: "inventory",
                table: "StockMovements",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountLines_InventoryCountId",
                schema: "inventory",
                table: "InventoryCountLines",
                column: "InventoryCountId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCountLines_ItemId",
                schema: "inventory",
                table: "InventoryCountLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_IsDeleted",
                schema: "inventory",
                table: "InventoryCounts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_Number",
                schema: "inventory",
                table: "InventoryCounts",
                column: "Number",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCounts_WarehouseId",
                schema: "inventory",
                table: "InventoryCounts",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_ItemId",
                schema: "inventory",
                table: "StockTransferLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_StockTransferId",
                schema: "inventory",
                table: "StockTransferLines",
                column: "StockTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_FromWarehouseId",
                schema: "inventory",
                table: "StockTransfers",
                column: "FromWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_IsDeleted",
                schema: "inventory",
                table: "StockTransfers",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_Number",
                schema: "inventory",
                table: "StockTransfers",
                column: "Number",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_ToWarehouseId",
                schema: "inventory",
                table: "StockTransfers",
                column: "ToWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseBalances_ItemId",
                schema: "inventory",
                table: "WarehouseBalances",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseBalances_WarehouseId_ItemId",
                schema: "inventory",
                table: "WarehouseBalances",
                columns: new[] { "WarehouseId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Code",
                schema: "inventory",
                table: "Warehouses",
                column: "Code",
                unique: true,
                filter: "\"Code\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_IsDeleted",
                schema: "inventory",
                table: "Warehouses",
                column: "IsDeleted");

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Warehouses_WarehouseId",
                schema: "inventory",
                table: "StockMovements",
                column: "WarehouseId",
                principalSchema: "inventory",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Warehouses_WarehouseId",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "InventoryCountLines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "StockTransferLines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "WarehouseBalances",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "InventoryCounts",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "StockTransfers",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "Warehouses",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_WarehouseId",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                schema: "inventory",
                table: "StockMovements");
        }
    }
}
