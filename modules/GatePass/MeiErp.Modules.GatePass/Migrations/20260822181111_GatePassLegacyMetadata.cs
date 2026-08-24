using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.GatePass.Migrations
{
    /// <inheritdoc />
    public partial class GatePassLegacyMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                schema: "gatepass",
                table: "Passes",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledUtc",
                schema: "gatepass",
                table: "Passes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyName",
                schema: "gatepass",
                table: "Passes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Department",
                schema: "gatepass",
                table: "Passes",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "gatepass",
                table: "Passes",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonCnic",
                schema: "gatepass",
                table: "Passes",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonPhone",
                schema: "gatepass",
                table: "Passes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNumber",
                schema: "gatepass",
                table: "Passes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceType",
                schema: "gatepass",
                table: "Passes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnReceivedByName",
                schema: "gatepass",
                table: "Passes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedUtc",
                schema: "gatepass",
                table: "Passes",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationReason",
                schema: "gatepass",
                table: "Passes");

            migrationBuilder.DropColumn(
                name: "CancelledUtc",
                schema: "gatepass",
                table: "Passes");

            migrationBuilder.DropColumn(
                name: "CompanyName",
                schema: "gatepass",
                table: "Passes");

            migrationBuilder.DropColumn(
                name: "Department",
                schema: "gatepass",
                table: "Passes");

            migrationBuilder.DropColumn(
                name: "Notes",
                schema: "gatepass",
                table: "Passes");

            migrationBuilder.DropColumn(
                name: "PersonCnic",
                schema: "gatepass",
                table: "Passes");

            migrationBuilder.DropColumn(
                name: "PersonPhone",
                schema: "gatepass",
                table: "Passes");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                schema: "gatepass",
                table: "Passes");

            migrationBuilder.DropColumn(
                name: "ReferenceType",
                schema: "gatepass",
                table: "Passes");

            migrationBuilder.DropColumn(
                name: "ReturnReceivedByName",
                schema: "gatepass",
                table: "Passes");

            migrationBuilder.DropColumn(
                name: "ReturnedUtc",
                schema: "gatepass",
                table: "Passes");
        }
    }
}
