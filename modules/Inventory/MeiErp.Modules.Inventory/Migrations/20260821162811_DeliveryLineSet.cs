using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.Inventory.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryLineSet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryLine_Deliveries_DeliveryId",
                schema: "inventory",
                table: "DeliveryLine");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DeliveryLine",
                schema: "inventory",
                table: "DeliveryLine");

            migrationBuilder.RenameTable(
                name: "DeliveryLine",
                schema: "inventory",
                newName: "DeliveryLines",
                newSchema: "inventory");

            migrationBuilder.RenameIndex(
                name: "IX_DeliveryLine_DeliveryId",
                schema: "inventory",
                table: "DeliveryLines",
                newName: "IX_DeliveryLines_DeliveryId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DeliveryLines",
                schema: "inventory",
                table: "DeliveryLines",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryLines_Deliveries_DeliveryId",
                schema: "inventory",
                table: "DeliveryLines",
                column: "DeliveryId",
                principalSchema: "inventory",
                principalTable: "Deliveries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryLines_Deliveries_DeliveryId",
                schema: "inventory",
                table: "DeliveryLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DeliveryLines",
                schema: "inventory",
                table: "DeliveryLines");

            migrationBuilder.RenameTable(
                name: "DeliveryLines",
                schema: "inventory",
                newName: "DeliveryLine",
                newSchema: "inventory");

            migrationBuilder.RenameIndex(
                name: "IX_DeliveryLines_DeliveryId",
                schema: "inventory",
                table: "DeliveryLine",
                newName: "IX_DeliveryLine_DeliveryId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DeliveryLine",
                schema: "inventory",
                table: "DeliveryLine",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryLine_Deliveries_DeliveryId",
                schema: "inventory",
                table: "DeliveryLine",
                column: "DeliveryId",
                principalSchema: "inventory",
                principalTable: "Deliveries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
