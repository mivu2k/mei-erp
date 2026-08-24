using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class DirectorFundAdvanceParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDirectorRequest",
                schema: "finance",
                table: "Advances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Advances_IsDirectorRequest",
                schema: "finance",
                table: "Advances",
                column: "IsDirectorRequest");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Advances_IsDirectorRequest",
                schema: "finance",
                table: "Advances");

            migrationBuilder.DropColumn(
                name: "IsDirectorRequest",
                schema: "finance",
                table: "Advances");
        }
    }
}
