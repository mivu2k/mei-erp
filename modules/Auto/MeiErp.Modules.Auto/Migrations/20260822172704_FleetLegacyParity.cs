using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.Auto.Migrations
{
    /// <inheritdoc />
    public partial class FleetLegacyParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                schema: "auto",
                table: "Vehicles",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "auto",
                table: "Vehicles",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Color",
                schema: "auto",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "Notes",
                schema: "auto",
                table: "Vehicles");
        }
    }
}
