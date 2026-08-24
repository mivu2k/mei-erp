using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.Tender.Migrations
{
    /// <inheritdoc />
    public partial class TenderItemAuditParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                schema: "tender",
                table: "TenderItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedUtc",
                schema: "tender",
                table: "TenderItems",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                schema: "tender",
                table: "TenderItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                schema: "tender",
                table: "TenderItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                schema: "tender",
                table: "TenderItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                schema: "tender",
                table: "TenderItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedUtc",
                schema: "tender",
                table: "TenderItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenderItems_IsDeleted",
                schema: "tender",
                table: "TenderItems",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenderItems_IsDeleted",
                schema: "tender",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "tender",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "CreatedUtc",
                schema: "tender",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                schema: "tender",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                schema: "tender",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                schema: "tender",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                schema: "tender",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "ModifiedUtc",
                schema: "tender",
                table: "TenderItems");
        }
    }
}
