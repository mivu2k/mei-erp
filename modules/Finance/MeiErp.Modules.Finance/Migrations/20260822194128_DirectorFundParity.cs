using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class DirectorFundParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDirectorRequest",
                schema: "finance",
                table: "PaymentRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_IsDirectorRequest",
                schema: "finance",
                table: "PaymentRequests",
                column: "IsDirectorRequest");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentRequests_IsDirectorRequest",
                schema: "finance",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "IsDirectorRequest",
                schema: "finance",
                table: "PaymentRequests");
        }
    }
}
