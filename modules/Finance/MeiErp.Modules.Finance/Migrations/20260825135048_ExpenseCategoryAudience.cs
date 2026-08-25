using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class ExpenseCategoryAudience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Audience",
                schema: "finance",
                table: "Accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Seeding only tags heads it creates, so an existing chart would
            // come through untagged and leave the requester's picker empty.
            // These are the standard heads somebody actually claims against;
            // anything else stays off the picker until a person decides it
            // belongs there.
            migrationBuilder.Sql("""
                UPDATE finance."Accounts" SET "Audience" = 1
                WHERE "Code" IN ('5220', '5230', '5530', '5540', '5600', '5900');

                UPDATE finance."Accounts" SET "Audience" = 2
                WHERE "Code" IN ('5410', '5420');
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Audience",
                schema: "finance",
                table: "Accounts");
        }
    }
}
