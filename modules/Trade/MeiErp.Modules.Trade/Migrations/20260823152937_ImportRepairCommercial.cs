using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.Trade.Migrations
{
    /// <summary>
    /// Brings the workshop's customers, quotations and orders into the one party
    /// master and the unified documents.
    ///
    /// The Repair migration that drops those tables runs AFTER this one - the
    /// host seeds Trade before Repair for exactly that reason - so this is the
    /// only chance to copy them. Every block is guarded on to_regclass, so an
    /// install that never had the workshop skips it rather than failing.
    ///
    /// Party ids are NOT preserved: Repair numbered its customers from 1 and so
    /// did Trade, so they would collide. Everything downstream is matched by
    /// name instead, which is also how the Repair migration relinks its jobs.
    /// </summary>
    public partial class ImportRepairCommercial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('repair."Customers"') IS NULL THEN RETURN; END IF;

                    -- A name already in the master is the same company, so it is
                    -- marked as a customer rather than duplicated.
                    UPDATE trade."Parties" p
                       SET "IsCustomer" = true
                      FROM repair."Customers" c
                     WHERE c."IsDeleted" = false AND p."Name" = c."Name" AND p."IsCustomer" = false;

                    INSERT INTO trade."Parties"
                        ("Code","Name","IsCustomer","IsSupplier","Phone","Email","Address",
                         "Notes","IsActive","CreatedUtc","CreatedBy","IsDeleted")
                    SELECT 'WC-'||c."Id", c."Name", true, false, c."Phone", c."Email", c."Address",
                           c."Notes", c."IsActive", c."CreatedUtc", c."CreatedBy", false
                      FROM repair."Customers" c
                     WHERE c."IsDeleted" = false
                       AND NOT EXISTS (SELECT 1 FROM trade."Parties" p WHERE p."Name" = c."Name");

                    PERFORM setval(pg_get_serial_sequence('trade."Parties"','Id'),
                        GREATEST((SELECT COALESCE(MAX("Id"),0) FROM trade."Parties"), 1));
                END $$;
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('repair."RepairQuotations"') IS NULL THEN RETURN; END IF;

                    -- Repair's four-state quotation maps onto the shared
                    -- lifecycle: Draft/Sent/Approved/Rejected -> 0/3/2/5.
                    INSERT INTO trade."Quotations"
                        ("Id","Number","Direction","Date","ValidUntil","PartyId","PartyName","DomainId",
                         "JobId","JobReference","Status","TaxPercent","Discount","Notes",
                         "CreatedUtc","CreatedBy","ModifiedUtc","ModifiedBy","IsDeleted","DeletedUtc","DeletedBy")
                    SELECT q."Id", q."Number", 0, q."Date", q."ValidUntil",
                           COALESCE((SELECT p."Id" FROM trade."Parties" p
                                      WHERE p."Name" = q."CustomerName" LIMIT 1), 0),
                           q."CustomerName", 0, q."JobId",
                           (SELECT j."Number" FROM repair."Jobs" j WHERE j."Id" = q."JobId"),
                           CASE q."Status" WHEN 0 THEN 0 WHEN 1 THEN 3 WHEN 2 THEN 2 ELSE 5 END,
                           q."TaxPercent", q."Discount", q."Notes",
                           q."CreatedUtc", q."CreatedBy", q."ModifiedUtc", q."ModifiedBy",
                           q."IsDeleted", q."DeletedUtc", q."DeletedBy"
                      FROM repair."RepairQuotations" q
                    ON CONFLICT ("Id") DO NOTHING;

                    INSERT INTO trade."QuotationLines"
                        ("Id","QuotationId","Description","Quantity","UnitPrice")
                    SELECT l."Id", l."RepairQuotationId", l."Description", l."Quantity", l."UnitPrice"
                      FROM repair."RepairQuotationLines" l
                    ON CONFLICT ("Id") DO NOTHING;

                    PERFORM setval(pg_get_serial_sequence('trade."Quotations"','Id'),
                        GREATEST((SELECT COALESCE(MAX("Id"),0) FROM trade."Quotations"), 1));
                    PERFORM setval(pg_get_serial_sequence('trade."QuotationLines"','Id'),
                        GREATEST((SELECT COALESCE(MAX("Id"),0) FROM trade."QuotationLines"), 1));
                END $$;
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('repair."RepairOrders"') IS NULL THEN RETURN; END IF;

                    -- A repair order was the bill, so it becomes a posted sales
                    -- invoice. What was collected against it carries over as the
                    -- settled amount, which is what makes the balance read right.
                    INSERT INTO trade."Invoices"
                        ("Id","Number","Direction","Date","PartyId","PartyName","DomainId",
                         "Status","TaxPercent","Discount","AmountSettled",
                         "CreatedUtc","CreatedBy","ModifiedUtc","ModifiedBy",
                         "IsDeleted","DeletedUtc","DeletedBy")
                    SELECT o."Id", o."Number", 0, o."Date",
                           COALESCE((SELECT p."Id" FROM trade."Parties" p
                                      WHERE p."Name" = o."CustomerName" LIMIT 1), 0),
                           o."CustomerName", 0,
                           7,                       -- Posted
                           0, o."Discount", o."AmountPaid",
                           o."CreatedUtc", o."CreatedBy", o."ModifiedUtc", o."ModifiedBy",
                           o."IsDeleted", o."DeletedUtc", o."DeletedBy"
                      FROM repair."RepairOrders" o
                    ON CONFLICT ("Id") DO NOTHING;

                    -- The order carried totals rather than lines, so the invoice
                    -- gets one line holding what was billed. Splitting it back
                    -- out would be inventing detail that was never stored.
                    INSERT INTO trade."InvoiceLines"
                        ("InvoiceId","Description","Quantity","UnitPrice")
                    SELECT o."Id",
                           COALESCE('Repair work - ' ||
                                    (SELECT j."Number" FROM repair."Jobs" j WHERE j."Id" = o."JobId"),
                                    'Repair work'),
                           1, o."Subtotal"
                      FROM repair."RepairOrders" o
                     WHERE NOT EXISTS (
                           SELECT 1 FROM trade."InvoiceLines" il WHERE il."InvoiceId" = o."Id");

                    PERFORM setval(pg_get_serial_sequence('trade."Invoices"','Id'),
                        GREATEST((SELECT COALESCE(MAX("Id"),0) FROM trade."Invoices"), 1));
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. By the time the Repair migration has run, the
            // rows this copied are the only copy there is, so undoing it would
            // delete live data rather than restore anything.
        }
    }
}
