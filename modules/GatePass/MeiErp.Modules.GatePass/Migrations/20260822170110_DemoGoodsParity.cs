using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.GatePass.Migrations
{
    /// <inheritdoc />
    public partial class DemoGoodsParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DemoIssuances",
                schema: "gatepass",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CustomerReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Department = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ReferenceLetter = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    IssuedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IssuedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    IssuedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpectedReturnOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReturnedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReturnCondition = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_DemoIssuances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DemoIssuanceItems",
                schema: "gatepass",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DemoIssuanceId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Accessories = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReturnedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoIssuanceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DemoIssuanceItems_DemoIssuances_DemoIssuanceId",
                        column: x => x.DemoIssuanceId,
                        principalSchema: "gatepass",
                        principalTable: "DemoIssuances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DemoIssuanceItems_DemoIssuanceId",
                schema: "gatepass",
                table: "DemoIssuanceItems",
                column: "DemoIssuanceId");

            migrationBuilder.CreateIndex(
                name: "IX_DemoIssuances_IsDeleted",
                schema: "gatepass",
                table: "DemoIssuances",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_DemoIssuances_Number",
                schema: "gatepass",
                table: "DemoIssuances",
                column: "Number",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_DemoIssuances_Status_ExpectedReturnOn",
                schema: "gatepass",
                table: "DemoIssuances",
                columns: new[] { "Status", "ExpectedReturnOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DemoIssuanceItems",
                schema: "gatepass");

            migrationBuilder.DropTable(
                name: "DemoIssuances",
                schema: "gatepass");
        }
    }
}
