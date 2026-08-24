using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.Tender.Migrations
{
    /// <inheritdoc />
    public partial class TenderLegacyCommercialParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "AwardDate",
                schema: "tender",
                table: "Tenders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AwardedValue",
                schema: "tender",
                table: "Tenders",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BidValidityDays",
                schema: "tender",
                table: "Tenders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompletionPeriodDays",
                schema: "tender",
                table: "Tenders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ContractEndDate",
                schema: "tender",
                table: "Tenders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ContractStartDate",
                schema: "tender",
                table: "Tenders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefectLiabilityPeriodMonths",
                schema: "tender",
                table: "Tenders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Department",
                schema: "tender",
                table: "Tenders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EmdAmount",
                schema: "tender",
                table: "Tenders",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmdExemptionReason",
                schema: "tender",
                table: "Tenders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FinancialOpeningDate",
                schema: "tender",
                table: "Tenders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEmdExempted",
                schema: "tender",
                table: "Tenders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "IssuingAuthority",
                schema: "tender",
                table: "Tenders",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTerms",
                schema: "tender",
                table: "Tenders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PortalReference",
                schema: "tender",
                table: "Tenders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubmissionMode",
                schema: "tender",
                table: "Tenders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TechnicalOpeningDate",
                schema: "tender",
                table: "Tenders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TenderFee",
                schema: "tender",
                table: "Tenders",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkOrderNumber",
                schema: "tender",
                table: "Tenders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Charges",
                schema: "tender",
                table: "Guarantees",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ClaimPeriodEndDate",
                schema: "tender",
                table: "Guarantees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseReference",
                schema: "tender",
                table: "Guarantees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remarks",
                schema: "tender",
                table: "Guarantees",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AwardDate",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "AwardedValue",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "BidValidityDays",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "CompletionPeriodDays",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "ContractEndDate",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "ContractStartDate",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "DefectLiabilityPeriodMonths",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "Department",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "EmdAmount",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "EmdExemptionReason",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "FinancialOpeningDate",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "IsEmdExempted",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "IssuingAuthority",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "PaymentTerms",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "PortalReference",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "SubmissionMode",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "TechnicalOpeningDate",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "TenderFee",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "WorkOrderNumber",
                schema: "tender",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "Charges",
                schema: "tender",
                table: "Guarantees");

            migrationBuilder.DropColumn(
                name: "ClaimPeriodEndDate",
                schema: "tender",
                table: "Guarantees");

            migrationBuilder.DropColumn(
                name: "ReleaseReference",
                schema: "tender",
                table: "Guarantees");

            migrationBuilder.DropColumn(
                name: "Remarks",
                schema: "tender",
                table: "Guarantees");
        }
    }
}
