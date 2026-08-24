using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Inventory.Migrations
{
    /// <inheritdoc />
    public partial class StockDomains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_Code",
                schema: "inventory",
                table: "Items");

            migrationBuilder.AddColumn<int>(
                name: "DomainId",
                schema: "inventory",
                table: "Warehouses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DomainId",
                schema: "inventory",
                table: "StockMovements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DomainId",
                schema: "inventory",
                table: "Items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "StockDomains",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_StockDomains", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_DomainId",
                schema: "inventory",
                table: "Warehouses",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_DomainId_Date",
                schema: "inventory",
                table: "StockMovements",
                columns: new[] { "DomainId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_DomainId_Code",
                schema: "inventory",
                table: "Items",
                columns: new[] { "DomainId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StockDomains_Code",
                schema: "inventory",
                table: "StockDomains",
                column: "Code",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StockDomains_IsDeleted",
                schema: "inventory",
                table: "StockDomains",
                column: "IsDeleted");

            // Create the two books and adopt everything that already exists into
            // the main one, BEFORE the foreign keys below start being enforced.
            //
            // Every existing row got DomainId = 0 from the AddColumn default,
            // and no domain has that id: without this backfill both AddForeignKey
            // calls below fail outright on any database that already holds items
            // or warehouses. A single undivided inventory *was* the main store,
            // so that is where its history belongs.
            migrationBuilder.Sql("""
                INSERT INTO inventory."StockDomains"
                    ("Code", "Name", "Description", "IsDefault", "IsActive", "CreatedUtc", "IsDeleted")
                VALUES
                    ('MAIN',  'Main Store',  'Goods the business buys and sells.',           true,  true, now(), false),
                    ('SPARE', 'Spare Parts', 'Parts the workshop consumes on repair jobs.',  false, true, now(), false);

                UPDATE inventory."Items"
                   SET "DomainId" = (SELECT "Id" FROM inventory."StockDomains" WHERE "Code" = 'MAIN')
                 WHERE "DomainId" = 0;

                UPDATE inventory."Warehouses"
                   SET "DomainId" = (SELECT "Id" FROM inventory."StockDomains" WHERE "Code" = 'MAIN')
                 WHERE "DomainId" = 0;

                UPDATE inventory."StockMovements"
                   SET "DomainId" = (SELECT "Id" FROM inventory."StockDomains" WHERE "Code" = 'MAIN')
                 WHERE "DomainId" = 0;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_StockDomains_DomainId",
                schema: "inventory",
                table: "Items",
                column: "DomainId",
                principalSchema: "inventory",
                principalTable: "StockDomains",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Warehouses_StockDomains_DomainId",
                schema: "inventory",
                table: "Warehouses",
                column: "DomainId",
                principalSchema: "inventory",
                principalTable: "StockDomains",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_StockDomains_DomainId",
                schema: "inventory",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Warehouses_StockDomains_DomainId",
                schema: "inventory",
                table: "Warehouses");

            migrationBuilder.DropTable(
                name: "StockDomains",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "IX_Warehouses_DomainId",
                schema: "inventory",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_DomainId_Date",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_Items_DomainId_Code",
                schema: "inventory",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "DomainId",
                schema: "inventory",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "DomainId",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "DomainId",
                schema: "inventory",
                table: "Items");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Code",
                schema: "inventory",
                table: "Items",
                column: "Code",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }
    }
}
