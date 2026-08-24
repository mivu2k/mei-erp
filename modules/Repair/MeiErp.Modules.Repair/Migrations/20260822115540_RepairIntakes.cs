using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Repair.Migrations
{
    /// <inheritdoc />
    public partial class RepairIntakes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Accessories",
                schema: "repair",
                table: "Jobs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Condition",
                schema: "repair",
                table: "Jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IntakeId",
                schema: "repair",
                table: "Jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                schema: "repair",
                table: "Jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RepairIntakes",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    ReceivedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedById = table.Column<string>(type: "text", nullable: false),
                    ReceivedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_RepairIntakes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepairIntakes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "repair",
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_IntakeId",
                schema: "repair",
                table: "Jobs",
                column: "IntakeId");

            migrationBuilder.CreateIndex(
                name: "IX_RepairIntakes_CustomerId",
                schema: "repair",
                table: "RepairIntakes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RepairIntakes_IsDeleted",
                schema: "repair",
                table: "RepairIntakes",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RepairIntakes_Number",
                schema: "repair",
                table: "RepairIntakes",
                column: "Number",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_RepairIntakes_IntakeId",
                schema: "repair",
                table: "Jobs",
                column: "IntakeId",
                principalSchema: "repair",
                principalTable: "RepairIntakes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_RepairIntakes_IntakeId",
                schema: "repair",
                table: "Jobs");

            migrationBuilder.DropTable(
                name: "RepairIntakes",
                schema: "repair");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_IntakeId",
                schema: "repair",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Accessories",
                schema: "repair",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Condition",
                schema: "repair",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "IntakeId",
                schema: "repair",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Priority",
                schema: "repair",
                table: "Jobs");
        }
    }
}
