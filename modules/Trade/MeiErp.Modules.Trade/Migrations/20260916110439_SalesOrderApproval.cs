using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.Trade.Migrations
{
    /// <inheritdoc />
    public partial class SalesOrderApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovalRequestId",
                schema: "trade",
                table: "SalesOrders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionComment",
                schema: "trade",
                table: "SalesOrders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovalRequestId",
                schema: "trade",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "DecisionComment",
                schema: "trade",
                table: "SalesOrders");
        }
    }
}
