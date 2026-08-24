using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Tender.Migrations
{
    /// <inheritdoc />
    public partial class TenderFilesAndMilestones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhysicalFiles",
                schema: "tender",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    FileNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OwnerType = table.Column<int>(type: "integer", nullable: false),
                    OwnerId = table.Column<int>(type: "integer", nullable: false),
                    OwnerReference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OwnerTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    HolderUserId = table.Column<string>(type: "text", nullable: true),
                    HolderName = table.Column<string>(type: "text", nullable: true),
                    Location = table.Column<string>(type: "text", nullable: true),
                    VolumeNumber = table.Column<string>(type: "text", nullable: true),
                    OpenedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ClosedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_PhysicalFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectMilestones",
                schema: "tender",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AchievedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PaymentAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_ProjectMilestones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectMilestones_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "tender",
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FileMovements",
                schema: "tender",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PhysicalFileId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    MovedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    FromHolderName = table.Column<string>(type: "text", nullable: true),
                    FromLocation = table.Column<string>(type: "text", nullable: true),
                    ToHolderUserId = table.Column<string>(type: "text", nullable: true),
                    ToHolderName = table.Column<string>(type: "text", nullable: true),
                    ToLocation = table.Column<string>(type: "text", nullable: true),
                    Purpose = table.Column<string>(type: "text", nullable: true),
                    DueBack = table.Column<DateOnly>(type: "date", nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    RecordedById = table.Column<string>(type: "text", nullable: false),
                    RecordedByName = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_FileMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileMovements_PhysicalFiles_PhysicalFileId",
                        column: x => x.PhysicalFileId,
                        principalSchema: "tender",
                        principalTable: "PhysicalFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileMovements_IsDeleted",
                schema: "tender",
                table: "FileMovements",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_FileMovements_PhysicalFileId_MovedOn",
                schema: "tender",
                table: "FileMovements",
                columns: new[] { "PhysicalFileId", "MovedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalFiles_FileNumber",
                schema: "tender",
                table: "PhysicalFiles",
                column: "FileNumber",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalFiles_IsDeleted",
                schema: "tender",
                table: "PhysicalFiles",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalFiles_OwnerType_OwnerId",
                schema: "tender",
                table: "PhysicalFiles",
                columns: new[] { "OwnerType", "OwnerId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMilestones_IsDeleted",
                schema: "tender",
                table: "ProjectMilestones",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMilestones_ProjectId_DueDate",
                schema: "tender",
                table: "ProjectMilestones",
                columns: new[] { "ProjectId", "DueDate" });

            migrationBuilder.Sql("""
                WITH owners AS (
                    SELECT 0 AS owner_type, "Id" AS owner_id, "Reference" AS owner_reference,
                           "Title" AS owner_title, COALESCE("PublishedOn", CURRENT_DATE) AS opened_on,
                           ROW_NUMBER() OVER (ORDER BY 0, "Id") AS sequence
                    FROM tender."Tenders" WHERE NOT "IsDeleted"
                    UNION ALL
                    SELECT 1, "Id", "Code", "Name", COALESCE("StartDate", CURRENT_DATE),
                           (SELECT COUNT(*) FROM tender."Tenders" WHERE NOT "IsDeleted")
                           + ROW_NUMBER() OVER (ORDER BY "Id")
                    FROM tender."Projects" WHERE NOT "IsDeleted"
                )
                INSERT INTO tender."PhysicalFiles"
                    ("FileNumber", "OwnerType", "OwnerId", "OwnerReference", "OwnerTitle", "Status",
                     "OpenedOn", "CreatedUtc", "IsDeleted")
                SELECT 'FILE-' || TO_CHAR(CURRENT_DATE, 'YY') || '-' || LPAD(sequence::text, 4, '0'),
                       owner_type, owner_id, owner_reference, owner_title, 0, opened_on,
                       CURRENT_TIMESTAMP, FALSE
                FROM owners;

                INSERT INTO tender."FileMovements"
                    ("PhysicalFileId", "Action", "MovedOn", "Remarks", "RecordedById", "RecordedByName",
                     "CreatedUtc", "IsDeleted")
                SELECT "Id", 0, "OpenedOn", 'File opened during registry migration.', '', 'System',
                       CURRENT_TIMESTAMP, FALSE
                FROM tender."PhysicalFiles";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileMovements",
                schema: "tender");

            migrationBuilder.DropTable(
                name: "ProjectMilestones",
                schema: "tender");

            migrationBuilder.DropTable(
                name: "PhysicalFiles",
                schema: "tender");
        }
    }
}
