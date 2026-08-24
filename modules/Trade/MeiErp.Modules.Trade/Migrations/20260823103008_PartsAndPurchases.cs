using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Trade.Migrations
{
    /// <inheritdoc />
    public partial class PartsAndPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartPurchases",
                schema: "trade",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PartyId = table.Column<int>(type: "integer", nullable: false),
                    PartyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SupplierInvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PurchasedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceivedById = table.Column<string>(type: "text", nullable: false),
                    ReceivedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    OtherCharges = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
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
                    table.PrimaryKey("PK_PartPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartPurchases_Parties_PartyId",
                        column: x => x.PartyId,
                        principalSchema: "trade",
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Parts",
                schema: "trade",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Sku = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SellingPrice = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    LastPurchaseCost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    AverageCost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    PurchasedQuantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    LastPurchasedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LastSupplierId = table.Column<int>(type: "integer", nullable: true),
                    LastSupplierName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_Parts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PartPurchaseLines",
                schema: "trade",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PartPurchaseId = table.Column<int>(type: "integer", nullable: false),
                    PartId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    NewSellingPrice = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartPurchaseLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartPurchaseLines_PartPurchases_PartPurchaseId",
                        column: x => x.PartPurchaseId,
                        principalSchema: "trade",
                        principalTable: "PartPurchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PartPurchaseLines_Parts_PartId",
                        column: x => x.PartId,
                        principalSchema: "trade",
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartPurchaseLines_PartId",
                schema: "trade",
                table: "PartPurchaseLines",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_PartPurchaseLines_PartPurchaseId",
                schema: "trade",
                table: "PartPurchaseLines",
                column: "PartPurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PartPurchases_IsDeleted",
                schema: "trade",
                table: "PartPurchases",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_PartPurchases_Number",
                schema: "trade",
                table: "PartPurchases",
                column: "Number",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_PartPurchases_PartyId",
                schema: "trade",
                table: "PartPurchases",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_PartPurchases_PurchasedOn",
                schema: "trade",
                table: "PartPurchases",
                column: "PurchasedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Parts_IsDeleted",
                schema: "trade",
                table: "Parts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Parts_Name",
                schema: "trade",
                table: "Parts",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Parts_Sku",
                schema: "trade",
                table: "Parts",
                column: "Sku",
                unique: true,
                filter: "\"Sku\" IS NOT NULL AND \"IsDeleted\" = false");

            // ---- carry the data across before the old tables are dropped ----
            //
            // Buying and selling were implemented twice: once in Inventory for
            // the main store, once in Repair for the workshop. Both move here.
            // The Inventory and Repair migrations that drop those tables run
            // AFTER this one - the host seeds Trade first for exactly that
            // reason - so this is the only chance to copy them.
            //
            // Every block is guarded on to_regclass so a fresh install, where
            // none of those tables ever existed, skips it rather than failing.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    -- The workshop's suppliers become supplier-side parties.
                    -- New ids, because Inventory's parties already own the low
                    -- ones; everything below remaps by name.
                    IF to_regclass('repair."RepairSuppliers"') IS NOT NULL THEN
                        INSERT INTO trade."Parties"
                            ("Code","Name","IsCustomer","IsSupplier","Phone","Email","Address",
                             "TaxNumber","Notes","IsActive","CreatedUtc","IsDeleted")
                        SELECT 'WS-'||s."Id", s."Name", false, true, s."Phone", s."Email", s."Address",
                               s."TaxNumber", s."Notes", true, now(), false
                          FROM repair."RepairSuppliers" s
                         WHERE s."IsDeleted" = false
                           AND NOT EXISTS (SELECT 1 FROM trade."Parties" p WHERE p."Name" = s."Name");

                        PERFORM setval(pg_get_serial_sequence('trade."Parties"','Id'),
                            GREATEST((SELECT COALESCE(MAX("Id"),0) FROM trade."Parties"), 1));
                    END IF;

                    IF to_regclass('repair."RepairParts"') IS NOT NULL THEN
                        INSERT INTO trade."Parts"
                            ("Id","Sku","Name","Brand","Model","SellingPrice","LastPurchaseCost",
                             "AverageCost","PurchasedQuantity","LastPurchasedOn","LastSupplierId",
                             "LastSupplierName","IsActive","CreatedUtc","CreatedBy","ModifiedUtc",
                             "ModifiedBy","IsDeleted","DeletedUtc","DeletedBy")
                        SELECT p."Id", p."Sku", p."Name", p."Brand", p."Model", p."SellingPrice",
                               p."LastPurchaseCost", p."AverageCost", p."PurchasedQuantity",
                               p."LastPurchasedOn",
                               (SELECT tp."Id" FROM trade."Parties" tp
                                  JOIN repair."RepairSuppliers" rs ON rs."Name" = tp."Name"
                                 WHERE rs."Id" = p."LastSupplierId" LIMIT 1),
                               (SELECT rs."Name" FROM repair."RepairSuppliers" rs
                                 WHERE rs."Id" = p."LastSupplierId"),
                               true, p."CreatedUtc", p."CreatedBy", p."ModifiedUtc",
                               p."ModifiedBy", p."IsDeleted", p."DeletedUtc", p."DeletedBy"
                          FROM repair."RepairParts" p
                        ON CONFLICT ("Id") DO NOTHING;

                        PERFORM setval(pg_get_serial_sequence('trade."Parts"','Id'),
                            GREATEST((SELECT COALESCE(MAX("Id"),0) FROM trade."Parts"), 1));
                    END IF;

                    IF to_regclass('repair."RepairPurchases"') IS NOT NULL THEN
                        INSERT INTO trade."PartPurchases"
                            ("Id","Number","PartyId","PartyName","SupplierInvoiceNumber","PurchasedOn",
                             "ReceivedById","ReceivedByName","TaxAmount","DiscountAmount","OtherCharges",
                             "Notes","CreatedUtc","CreatedBy","ModifiedUtc","ModifiedBy",
                             "IsDeleted","DeletedUtc","DeletedBy")
                        SELECT r."Id", r."Number",
                               COALESCE((SELECT tp."Id" FROM trade."Parties" tp
                                           JOIN repair."RepairSuppliers" rs ON rs."Name" = tp."Name"
                                          WHERE rs."Id" = r."SupplierId" LIMIT 1), 0),
                               COALESCE((SELECT rs."Name" FROM repair."RepairSuppliers" rs
                                          WHERE rs."Id" = r."SupplierId"), ''),
                               r."SupplierInvoiceNumber", r."PurchasedOn", r."ReceivedById",
                               r."ReceivedByName", r."TaxAmount", r."DiscountAmount", r."OtherCharges",
                               r."Notes", r."CreatedUtc", r."CreatedBy", r."ModifiedUtc",
                               r."ModifiedBy", r."IsDeleted", r."DeletedUtc", r."DeletedBy"
                          FROM repair."RepairPurchases" r
                        ON CONFLICT ("Id") DO NOTHING;

                        INSERT INTO trade."PartPurchaseLines"
                            ("Id","PartPurchaseId","PartId","Quantity","UnitCost","NewSellingPrice","Remarks")
                        SELECT l."Id", l."RepairPurchaseId", l."PartId", l."Quantity", l."UnitCost",
                               l."NewSellingPrice", l."Remarks"
                          FROM repair."RepairPurchaseLines" l
                        ON CONFLICT ("Id") DO NOTHING;

                        PERFORM setval(pg_get_serial_sequence('trade."PartPurchases"','Id'),
                            GREATEST((SELECT COALESCE(MAX("Id"),0) FROM trade."PartPurchases"), 1));
                        PERFORM setval(pg_get_serial_sequence('trade."PartPurchaseLines"','Id'),
                            GREATEST((SELECT COALESCE(MAX("Id"),0) FROM trade."PartPurchaseLines"), 1));
                    END IF;
                END $$;
                """);

            // Inventory's own commercial documents. Ids are preserved, so every
            // line still points at its header and no document number changes.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('inventory."PurchaseOrders"') IS NULL THEN RETURN; END IF;

                    INSERT INTO trade."PurchaseOrders"
                        ("Id","Number","Date","PartyId","PartyName","DomainId","Status","Notes",
                         "ApprovalRequestId","DecisionComment","CreatedUtc","CreatedBy",
                         "ModifiedUtc","ModifiedBy","IsDeleted","DeletedUtc","DeletedBy")
                    SELECT "Id","Number","Date","PartyId","PartyName",0,"Status","Notes",
                           "ApprovalRequestId","DecisionComment","CreatedUtc","CreatedBy",
                           "ModifiedUtc","ModifiedBy","IsDeleted","DeletedUtc","DeletedBy"
                      FROM inventory."PurchaseOrders"
                    ON CONFLICT ("Id") DO NOTHING;

                    INSERT INTO trade."PurchaseOrderLines"
                        ("Id","PurchaseOrderId","ItemId","ItemCode","ItemName","Quantity","UnitCost","Received")
                    SELECT "Id","PurchaseOrderId","ItemId","ItemCode","ItemName","Quantity","UnitCost","Received"
                      FROM inventory."PurchaseOrderLines"
                    ON CONFLICT ("Id") DO NOTHING;

                    INSERT INTO trade."GoodsReceipts"
                        ("Id","Number","Date","PurchaseOrderId","PartyId","PartyName","Notes",
                         "CreatedUtc","CreatedBy","ModifiedUtc","ModifiedBy","IsDeleted","DeletedUtc","DeletedBy")
                    SELECT "Id","Number","Date","PurchaseOrderId","PartyId","PartyName","Notes",
                           "CreatedUtc","CreatedBy","ModifiedUtc","ModifiedBy","IsDeleted","DeletedUtc","DeletedBy"
                      FROM inventory."GoodsReceipts"
                    ON CONFLICT ("Id") DO NOTHING;

                    -- Inventory named this one in the singular; Trade pluralises
                    -- it. Resolved at run time so either shape copies across.
                    IF to_regclass('inventory."GoodsReceiptLine"') IS NOT NULL THEN
                        INSERT INTO trade."GoodsReceiptLines"
                            ("Id","GoodsReceiptId","ItemId","ItemCode","ItemName","Quantity","UnitCost")
                        SELECT "Id","GoodsReceiptId","ItemId","ItemCode","ItemName","Quantity","UnitCost"
                          FROM inventory."GoodsReceiptLine"
                        ON CONFLICT ("Id") DO NOTHING;
                    ELSIF to_regclass('inventory."GoodsReceiptLines"') IS NOT NULL THEN
                        INSERT INTO trade."GoodsReceiptLines"
                            ("Id","GoodsReceiptId","ItemId","ItemCode","ItemName","Quantity","UnitCost")
                        SELECT "Id","GoodsReceiptId","ItemId","ItemCode","ItemName","Quantity","UnitCost"
                          FROM inventory."GoodsReceiptLines"
                        ON CONFLICT ("Id") DO NOTHING;
                    END IF;

                    INSERT INTO trade."SalesOrders"
                        ("Id","Number","Date","PartyId","PartyName","DomainId","Status","Notes",
                         "CreatedUtc","CreatedBy","ModifiedUtc","ModifiedBy",
                         "IsDeleted","DeletedUtc","DeletedBy")
                    SELECT "Id","Number","Date","PartyId","PartyName",0,"Status","Notes",
                           "CreatedUtc","CreatedBy","ModifiedUtc","ModifiedBy",
                           "IsDeleted","DeletedUtc","DeletedBy"
                      FROM inventory."SalesOrders"
                    ON CONFLICT ("Id") DO NOTHING;

                    INSERT INTO trade."SalesOrderLines"
                        ("Id","SalesOrderId","ItemId","ItemCode","ItemName","Quantity","UnitPrice",
                         "Delivered","UnitCost")
                    SELECT "Id","SalesOrderId","ItemId","ItemCode","ItemName","Quantity","UnitPrice",
                           "Delivered","UnitCost"
                      FROM inventory."SalesOrderLines"
                    ON CONFLICT ("Id") DO NOTHING;

                    INSERT INTO trade."Deliveries"
                        ("Id","Number","Date","SalesOrderId","PartyId","PartyName","CollectedBy","Notes",
                         "CreatedUtc","CreatedBy","ModifiedUtc","ModifiedBy","IsDeleted","DeletedUtc","DeletedBy")
                    SELECT "Id","Number","Date","SalesOrderId","PartyId","PartyName","CollectedBy","Notes",
                           "CreatedUtc","CreatedBy","ModifiedUtc","ModifiedBy","IsDeleted","DeletedUtc","DeletedBy"
                      FROM inventory."Deliveries"
                    ON CONFLICT ("Id") DO NOTHING;

                    INSERT INTO trade."DeliveryLines"
                        ("Id","DeliveryId","ItemId","ItemCode","ItemName","Quantity","UnitPrice","UnitCost")
                    SELECT "Id","DeliveryId","ItemId","ItemCode","ItemName","Quantity","UnitPrice","UnitCost"
                      FROM inventory."DeliveryLines"
                    ON CONFLICT ("Id") DO NOTHING;

                    -- Every copied document belongs to the main store: that is
                    -- what a single undivided inventory was.
                    UPDATE trade."PurchaseOrders" SET "DomainId" =
                        COALESCE((SELECT "Id" FROM inventory."StockDomains" WHERE "Code"='MAIN'),0)
                      WHERE "DomainId" = 0;
                    UPDATE trade."SalesOrders" SET "DomainId" =
                        COALESCE((SELECT "Id" FROM inventory."StockDomains" WHERE "Code"='MAIN'),0)
                      WHERE "DomainId" = 0;

                    PERFORM setval(pg_get_serial_sequence('trade."PurchaseOrders"','Id'),
                        GREATEST((SELECT COALESCE(MAX("Id"),0) FROM trade."PurchaseOrders"), 1));
                    PERFORM setval(pg_get_serial_sequence('trade."SalesOrders"','Id'),
                        GREATEST((SELECT COALESCE(MAX("Id"),0) FROM trade."SalesOrders"), 1));
                    PERFORM setval(pg_get_serial_sequence('trade."GoodsReceipts"','Id'),
                        GREATEST((SELECT COALESCE(MAX("Id"),0) FROM trade."GoodsReceipts"), 1));
                    PERFORM setval(pg_get_serial_sequence('trade."Deliveries"','Id'),
                        GREATEST((SELECT COALESCE(MAX("Id"),0) FROM trade."Deliveries"), 1));
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartPurchaseLines",
                schema: "trade");

            migrationBuilder.DropTable(
                name: "PartPurchases",
                schema: "trade");

            migrationBuilder.DropTable(
                name: "Parts",
                schema: "trade");
        }
    }
}
