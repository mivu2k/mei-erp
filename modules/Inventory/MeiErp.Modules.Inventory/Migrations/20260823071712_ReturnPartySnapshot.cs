using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.Inventory.Migrations
{
    /// <inheritdoc />
    public partial class ReturnPartySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryReturns_Parties_PartyId",
                schema: "inventory",
                table: "InventoryReturns");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_InventoryReturns_Parties_PartyId",
                schema: "inventory",
                table: "InventoryReturns",
                column: "PartyId",
                principalSchema: "inventory",
                principalTable: "Parties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
