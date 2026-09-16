using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Platform.Identity.Migrations
{
    /// <inheritdoc />
    public partial class UserHomeAndConfigurablePdfLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PrintLandscape",
                schema: "platform",
                table: "CompanyProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "PrintMarginMm",
                schema: "platform",
                table: "CompanyProfiles",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 14m);

            migrationBuilder.AddColumn<string>(
                name: "PrintPageSize",
                schema: "platform",
                table: "CompanyProfiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "A4");

            migrationBuilder.AddColumn<bool>(
                name: "PrintShowCompanyDetails",
                schema: "platform",
                table: "CompanyProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PrintShowFooter",
                schema: "platform",
                table: "CompanyProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PrintShowLogo",
                schema: "platform",
                table: "CompanyProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // This column was released just before the PDF settings. IF NOT
            // EXISTS keeps upgrades safe while still building a fresh DB.
            migrationBuilder.Sql("""
                ALTER TABLE platform."AspNetUsers"
                ADD COLUMN IF NOT EXISTS "PreferredHomePath" character varying(300);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrintLandscape",
                schema: "platform",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "PrintMarginMm",
                schema: "platform",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "PrintPageSize",
                schema: "platform",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "PrintShowCompanyDetails",
                schema: "platform",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "PrintShowFooter",
                schema: "platform",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "PrintShowLogo",
                schema: "platform",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "PreferredHomePath",
                schema: "platform",
                table: "AspNetUsers");
        }
    }
}
