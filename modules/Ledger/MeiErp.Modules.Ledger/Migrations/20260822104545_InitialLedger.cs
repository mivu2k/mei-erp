using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class InitialLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ledger");

            migrationBuilder.CreateTable(
                name: "Heads",
                schema: "ledger",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ParentHeadId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_Heads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Heads_Heads_ParentHeadId",
                        column: x => x.ParentHeadId,
                        principalSchema: "ledger",
                        principalTable: "Heads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "ledger",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DispatchedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    DeadLetteredUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CausedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ledgers",
                schema: "ledger",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CounterpartyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CounterpartyPhone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CounterpartyAddress = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Nature = table.Column<int>(type: "integer", nullable: false),
                    ParentLedgerId = table.Column<int>(type: "integer", nullable: true),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 2, nullable: false),
                    OpenedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    HeadId = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_Ledgers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ledgers_Heads_HeadId",
                        column: x => x.HeadId,
                        principalSchema: "ledger",
                        principalTable: "Heads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Ledgers_Ledgers_ParentLedgerId",
                        column: x => x.ParentLedgerId,
                        principalSchema: "ledger",
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Entries",
                schema: "ledger",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlainLedgerId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    CounterLedgerId = table.Column<int>(type: "integer", nullable: true),
                    TransferGroup = table.Column<Guid>(type: "uuid", nullable: true),
                    HeadId = table.Column<int>(type: "integer", nullable: true),
                    RecordedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RecordedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
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
                    table.PrimaryKey("PK_Entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Entries_Heads_HeadId",
                        column: x => x.HeadId,
                        principalSchema: "ledger",
                        principalTable: "Heads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Entries_Ledgers_CounterLedgerId",
                        column: x => x.CounterLedgerId,
                        principalSchema: "ledger",
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Entries_Ledgers_PlainLedgerId",
                        column: x => x.PlainLedgerId,
                        principalSchema: "ledger",
                        principalTable: "Ledgers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Entries_CounterLedgerId",
                schema: "ledger",
                table: "Entries",
                column: "CounterLedgerId");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_Date",
                schema: "ledger",
                table: "Entries",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_HeadId",
                schema: "ledger",
                table: "Entries",
                column: "HeadId");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_IsDeleted",
                schema: "ledger",
                table: "Entries",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_PlainLedgerId",
                schema: "ledger",
                table: "Entries",
                column: "PlainLedgerId");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_TransferGroup",
                schema: "ledger",
                table: "Entries",
                column: "TransferGroup");

            migrationBuilder.CreateIndex(
                name: "IX_Heads_IsDeleted",
                schema: "ledger",
                table: "Heads",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Heads_Name",
                schema: "ledger",
                table: "Heads",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Heads_ParentHeadId",
                schema: "ledger",
                table: "Heads",
                column: "ParentHeadId");

            migrationBuilder.CreateIndex(
                name: "IX_Ledgers_HeadId",
                schema: "ledger",
                table: "Ledgers",
                column: "HeadId");

            migrationBuilder.CreateIndex(
                name: "IX_Ledgers_IsDeleted",
                schema: "ledger",
                table: "Ledgers",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Ledgers_Name",
                schema: "ledger",
                table: "Ledgers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Ledgers_ParentLedgerId",
                schema: "ledger",
                table: "Ledgers",
                column: "ParentLedgerId");

            migrationBuilder.CreateIndex(
                name: "IX_Ledgers_Status",
                schema: "ledger",
                table: "Ledgers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_DispatchedUtc_OccurredUtc",
                schema: "ledger",
                table: "outbox_messages",
                columns: new[] { "DispatchedUtc", "OccurredUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Entries",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "Ledgers",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "Heads",
                schema: "ledger");
        }
    }
}
