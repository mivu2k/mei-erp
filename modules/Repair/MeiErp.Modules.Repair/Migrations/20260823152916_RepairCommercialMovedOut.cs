using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Repair.Migrations
{
    /// <inheritdoc />
    public partial class RepairCommercialMovedOut : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The snapshot column has to exist before the relink below can
            // fill it, and both have to happen before the customer table goes.
            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                schema: "repair",
                table: "RepairIntakes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // Relink jobs and intakes to the party master BEFORE the workshop's
            // own customer table goes.
            //
            // Trade's ImportRepairCommercial migration has already copied these
            // customers across (the host seeds Trade first), but with new ids -
            // Repair and Trade both numbered from 1. Matching by name is what
            // reconnects them; anything that finds no match keeps its old id and
            // its name snapshot, which reads correctly even though it no longer
            // resolves.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('trade."Parties"') IS NULL THEN RETURN; END IF;

                    UPDATE repair."RepairIntakes" i
                       SET "CustomerName" = c."Name"
                      FROM repair."Customers" c
                     WHERE c."Id" = i."CustomerId";

                    UPDATE repair."Jobs" j
                       SET "CustomerId" = p."Id"
                      FROM trade."Parties" p
                     WHERE p."Name" = j."CustomerName";

                    UPDATE repair."RepairIntakes" i
                       SET "CustomerId" = p."Id"
                      FROM trade."Parties" p
                     WHERE p."Name" = i."CustomerName";
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Customers_CustomerId",
                schema: "repair",
                table: "Jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_RepairIntakes_Customers_CustomerId",
                schema: "repair",
                table: "RepairIntakes");

            migrationBuilder.DropTable(
                name: "Customers",
                schema: "repair");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerName",
                schema: "repair",
                table: "RepairIntakes");

            migrationBuilder.CreateTable(
                name: "Customers",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Address = table.Column<string>(type: "text", nullable: true),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CommunicationPreference = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedBy = table.Column<string>(type: "text", nullable: true),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Organization = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RepairQuotations",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IntakeId = table.Column<int>(type: "integer", nullable: true),
                    JobId = table.Column<int>(type: "integer", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CustomerApproved = table.Column<bool>(type: "boolean", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DeletedBy = table.Column<string>(type: "text", nullable: true),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Discount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerApproved = table.Column<bool>(type: "boolean", nullable: false),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TaxPercent = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
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
                    table.ForeignKey(
                        name: "FK_RepairQuotations_RepairIntakes_IntakeId",
                        column: x => x.IntakeId,
                        principalSchema: "repair",
                        principalTable: "RepairIntakes",
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
                    QuotationId = table.Column<int>(type: "integer", nullable: false),
                    AmountPaid = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DeletedBy = table.Column<string>(type: "text", nullable: true),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Discount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    IntakeId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    JobId = table.Column<int>(type: "integer", nullable: true),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PaymentStatus = table.Column<int>(type: "integer", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Tax = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
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
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
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
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DeletedBy = table.Column<string>(type: "text", nullable: true),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
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
                name: "IX_Customers_Code",
                schema: "repair",
                table: "Customers",
                column: "Code",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_IsDeleted",
                schema: "repair",
                table: "Customers",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Name",
                schema: "repair",
                table: "Customers",
                column: "Name");

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
                name: "IX_RepairQuotations_IntakeId",
                schema: "repair",
                table: "RepairQuotations",
                column: "IntakeId");

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

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Customers_CustomerId",
                schema: "repair",
                table: "Jobs",
                column: "CustomerId",
                principalSchema: "repair",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RepairIntakes_Customers_CustomerId",
                schema: "repair",
                table: "RepairIntakes",
                column: "CustomerId",
                principalSchema: "repair",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
