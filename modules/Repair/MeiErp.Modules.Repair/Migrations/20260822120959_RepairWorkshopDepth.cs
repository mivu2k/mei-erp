using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Repair.Migrations
{
    /// <inheritdoc />
    public partial class RepairWorkshopDepth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Symptoms",
                schema: "repair",
                table: "Jobs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RepairCatalogItems",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepairCatalogItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RepairDiagnoses",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobId = table.Column<int>(type: "integer", nullable: false),
                    TechnicianId = table.Column<string>(type: "text", nullable: false),
                    TechnicianName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Findings = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RequiredParts = table.Column<string>(type: "text", nullable: true),
                    RequiredLabour = table.Column<string>(type: "text", nullable: true),
                    EstimatedDays = table.Column<int>(type: "integer", nullable: true),
                    EstimatedHours = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    WorkPerformed = table.Column<string>(type: "text", nullable: true),
                    InternalNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepairDiagnoses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepairDiagnoses_Jobs_JobId",
                        column: x => x.JobId,
                        principalSchema: "repair",
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RepairStatusHistory",
                schema: "repair",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobId = table.Column<int>(type: "integer", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: false),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    ChangedById = table.Column<string>(type: "text", nullable: false),
                    ChangedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ChangedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepairStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepairStatusHistory_Jobs_JobId",
                        column: x => x.JobId,
                        principalSchema: "repair",
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RepairCatalogItems_IsDeleted",
                schema: "repair",
                table: "RepairCatalogItems",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RepairCatalogItems_Kind_Name",
                schema: "repair",
                table: "RepairCatalogItems",
                columns: new[] { "Kind", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_RepairDiagnoses_IsDeleted",
                schema: "repair",
                table: "RepairDiagnoses",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RepairDiagnoses_JobId",
                schema: "repair",
                table: "RepairDiagnoses",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_RepairStatusHistory_JobId",
                schema: "repair",
                table: "RepairStatusHistory",
                column: "JobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepairCatalogItems",
                schema: "repair");

            migrationBuilder.DropTable(
                name: "RepairDiagnoses",
                schema: "repair");

            migrationBuilder.DropTable(
                name: "RepairStatusHistory",
                schema: "repair");

            migrationBuilder.DropColumn(
                name: "Symptoms",
                schema: "repair",
                table: "Jobs");
        }
    }
}
