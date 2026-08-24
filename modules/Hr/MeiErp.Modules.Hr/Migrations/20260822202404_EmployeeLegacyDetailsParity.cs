using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.Hr.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeLegacyDetailsParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                schema: "hr",
                table: "Employees",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "hr",
                table: "Employees",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Designation",
                schema: "hr",
                table: "Employees",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DepartmentName",
                schema: "hr",
                table: "Employees",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Cnic",
                schema: "hr",
                table: "Employees",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                schema: "hr",
                table: "Employees",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlternatePhone",
                schema: "hr",
                table: "Employees",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                schema: "hr",
                table: "Employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountTitle",
                schema: "hr",
                table: "Employees",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                schema: "hr",
                table: "Employees",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BloodGroup",
                schema: "hr",
                table: "Employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                schema: "hr",
                table: "Employees",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ConfirmedOn",
                schema: "hr",
                table: "Employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                schema: "hr",
                table: "Employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactName",
                schema: "hr",
                table: "Employees",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactPhone",
                schema: "hr",
                table: "Employees",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmploymentType",
                schema: "hr",
                table: "Employees",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FatherName",
                schema: "hr",
                table: "Employees",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Gender",
                schema: "hr",
                table: "Employees",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LeavingReason",
                schema: "hr",
                table: "Employees",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaritalStatus",
                schema: "hr",
                table: "Employees",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "hr",
                table: "Employees",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportsToEmployeeCode",
                schema: "hr",
                table: "Employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SocialSecurityNumber",
                schema: "hr",
                table: "Employees",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxNumber",
                schema: "hr",
                table: "Employees",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkLocation",
                schema: "hr",
                table: "Employees",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "AlternatePhone",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "BankAccountTitle",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "BankName",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "BloodGroup",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "City",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "ConfirmedOn",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "EmergencyContactName",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "EmergencyContactPhone",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "EmploymentType",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "FatherName",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "Gender",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "LeavingReason",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "MaritalStatus",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "Notes",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "ReportsToEmployeeCode",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "SocialSecurityNumber",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "TaxNumber",
                schema: "hr",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "WorkLocation",
                schema: "hr",
                table: "Employees");

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                schema: "hr",
                table: "Employees",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "hr",
                table: "Employees",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Designation",
                schema: "hr",
                table: "Employees",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DepartmentName",
                schema: "hr",
                table: "Employees",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Cnic",
                schema: "hr",
                table: "Employees",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);
        }
    }
}
