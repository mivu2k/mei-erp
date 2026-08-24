using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Modules.Tender.Migrations
{
    /// <inheritdoc />
    public partial class TenderProjectParity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ActualHours",
                schema: "tender",
                table: "ProjectTasks",
                type: "numeric(18,4)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedHours",
                schema: "tender",
                table: "ProjectTasks",
                type: "numeric(18,4)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "tender",
                table: "ProjectTasks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                schema: "tender",
                table: "ProjectTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                schema: "tender",
                table: "ProjectTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                schema: "tender",
                table: "ProjectTasks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Budget",
                schema: "tender",
                table: "Projects",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                schema: "tender",
                table: "Projects",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPerson",
                schema: "tender",
                table: "Projects",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                schema: "tender",
                table: "Projects",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "tender",
                table: "Projects",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                schema: "tender",
                table: "Projects",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "tender",
                table: "Projects",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                schema: "tender",
                table: "Projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualHours",
                schema: "tender",
                table: "ProjectTasks");

            migrationBuilder.DropColumn(
                name: "EstimatedHours",
                schema: "tender",
                table: "ProjectTasks");

            migrationBuilder.DropColumn(
                name: "Notes",
                schema: "tender",
                table: "ProjectTasks");

            migrationBuilder.DropColumn(
                name: "Priority",
                schema: "tender",
                table: "ProjectTasks");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                schema: "tender",
                table: "ProjectTasks");

            migrationBuilder.DropColumn(
                name: "StartDate",
                schema: "tender",
                table: "ProjectTasks");

            migrationBuilder.DropColumn(
                name: "Budget",
                schema: "tender",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                schema: "tender",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ContactPerson",
                schema: "tender",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                schema: "tender",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "tender",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Location",
                schema: "tender",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Notes",
                schema: "tender",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Priority",
                schema: "tender",
                table: "Projects");
        }
    }
}
