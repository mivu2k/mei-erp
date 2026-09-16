using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeiErp.Platform.Identity.Migrations
{
    /// <inheritdoc />
    public partial class DesignationMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Designations",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Designations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Designations_Name",
                schema: "platform",
                table: "Designations",
                column: "Name",
                unique: true);

            // Preserve the titles already assigned to users so introducing the
            // controlled list does not make existing designations disappear.
            migrationBuilder.Sql("""
                INSERT INTO platform."Designations" ("Id", "Name", "Code", "IsActive")
                SELECT gen_random_uuid()::text, titles."Designation", NULL, TRUE
                FROM (
                    SELECT DISTINCT BTRIM("Designation") AS "Designation"
                    FROM platform."AspNetUsers"
                    WHERE "Designation" IS NOT NULL AND BTRIM("Designation") <> ''
                ) AS titles
                ON CONFLICT ("Name") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Designations",
                schema: "platform");
        }
    }
}
