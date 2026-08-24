using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.Tender.Migrations
{
    /// <inheritdoc />
    public partial class TenderLegacyDetailParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                schema: "tender",
                table: "Tenders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPerson",
                schema: "tender",
                table: "Tenders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                schema: "tender",
                table: "Tenders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "tender",
                table: "Tenders",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "L1Amount",
                schema: "tender",
                table: "Tenders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OurRank",
                schema: "tender",
                table: "Tenders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PerformanceGuaranteePercentage",
                schema: "tender",
                table: "Tenders",
                type: "numeric(18,4)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RetentionMoneyPercentage",
                schema: "tender",
                table: "Tenders",
                type: "numeric(18,4)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Brand",
                schema: "tender",
                table: "TenderItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryOfOrigin",
                schema: "tender",
                table: "TenderItems",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryDays",
                schema: "tender",
                table: "TenderItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedRate",
                schema: "tender",
                table: "TenderItems",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemCode",
                schema: "tender",
                table: "TenderItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remarks",
                schema: "tender",
                table: "TenderItems",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                schema: "tender",
                table: "TenderItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Specification",
                schema: "tender",
                table: "TenderItems",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankContactPerson",
                schema: "tender",
                table: "Guarantees",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankContactPhone",
                schema: "tender",
                table: "Guarantees",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BranchName",
                schema: "tender",
                table: "Guarantees",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InstrumentType",
                schema: "tender",
                table: "Guarantees",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RenewalOfGuaranteeId",
                schema: "tender",
                table: "Guarantees",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "tender",
                table: "Guarantees",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactEmail",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "ContactPerson",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "L1Amount",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "OurRank",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "PerformanceGuaranteePercentage",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "RetentionMoneyPercentage",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "Brand",
                schema: "tender",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "CountryOfOrigin",
                schema: "tender",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "DeliveryDays",
                schema: "tender",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "EstimatedRate",
                schema: "tender",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "ItemCode",
                schema: "tender",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "Remarks",
                schema: "tender",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                schema: "tender",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "Specification",
                schema: "tender",
                table: "TenderItems");

            migrationBuilder.DropColumn(
                name: "BankContactPerson",
                schema: "tender",
                table: "Guarantees");

            migrationBuilder.DropColumn(
                name: "BankContactPhone",
                schema: "tender",
                table: "Guarantees");

            migrationBuilder.DropColumn(
                name: "BranchName",
                schema: "tender",
                table: "Guarantees");

            migrationBuilder.DropColumn(
                name: "InstrumentType",
                schema: "tender",
                table: "Guarantees");

            migrationBuilder.DropColumn(
                name: "RenewalOfGuaranteeId",
                schema: "tender",
                table: "Guarantees");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "tender",
                table: "Guarantees");
        }
    }
}
