using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.Repair.Migrations
{
    /// <inheritdoc />
    public partial class RepairParityCloseout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "JobId",
                schema: "repair",
                table: "RepairQuotations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "IntakeId",
                schema: "repair",
                table: "RepairQuotations",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "JobId",
                schema: "repair",
                table: "RepairOrders",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "IntakeId",
                schema: "repair",
                table: "RepairOrders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentBasis",
                schema: "repair",
                table: "RepairIntakes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CollectedByCnic",
                schema: "repair",
                table: "Jobs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CollectedByPhone",
                schema: "repair",
                table: "Jobs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveredByName",
                schema: "repair",
                table: "Jobs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryNote",
                schema: "repair",
                table: "Jobs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CommunicationPreference",
                schema: "repair",
                table: "Customers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "repair",
                table: "Customers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Organization",
                schema: "repair",
                table: "Customers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepairQuotations_IntakeId",
                schema: "repair",
                table: "RepairQuotations",
                column: "IntakeId");

            migrationBuilder.AddForeignKey(
                name: "FK_RepairQuotations_RepairIntakes_IntakeId",
                schema: "repair",
                table: "RepairQuotations",
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
                name: "FK_RepairQuotations_RepairIntakes_IntakeId",
                schema: "repair",
                table: "RepairQuotations");

            migrationBuilder.DropIndex(
                name: "IX_RepairQuotations_IntakeId",
                schema: "repair",
                table: "RepairQuotations");

            migrationBuilder.DropColumn(
                name: "IntakeId",
                schema: "repair",
                table: "RepairQuotations");

            migrationBuilder.DropColumn(
                name: "IntakeId",
                schema: "repair",
                table: "RepairOrders");

            migrationBuilder.DropColumn(
                name: "PaymentBasis",
                schema: "repair",
                table: "RepairIntakes");

            migrationBuilder.DropColumn(
                name: "CollectedByCnic",
                schema: "repair",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CollectedByPhone",
                schema: "repair",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "DeliveredByName",
                schema: "repair",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "DeliveryNote",
                schema: "repair",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CommunicationPreference",
                schema: "repair",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Notes",
                schema: "repair",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Organization",
                schema: "repair",
                table: "Customers");

            migrationBuilder.AlterColumn<int>(
                name: "JobId",
                schema: "repair",
                table: "RepairQuotations",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "JobId",
                schema: "repair",
                table: "RepairOrders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
