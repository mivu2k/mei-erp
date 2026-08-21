using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class InitialFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "Accounts",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    IsPostable = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Accounts_Accounts_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "finance",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_sequences",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Prefix = table.Column<string>(type: "text", nullable: false),
                    Next = table.Column<int>(type: "integer", nullable: false),
                    Padding = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_sequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FiscalYears",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    ClosedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedBy = table.Column<string>(type: "text", nullable: true),
                    ClosingVoucherId = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_FiscalYears", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "finance",
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
                name: "Vouchers",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Narration = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SourceModule = table.Column<string>(type: "text", nullable: true),
                    SourceDocumentType = table.Column<string>(type: "text", nullable: true),
                    SourceDocumentId = table.Column<int>(type: "integer", nullable: true),
                    SourceReference = table.Column<string>(type: "text", nullable: true),
                    PostedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PostedBy = table.Column<string>(type: "text", nullable: true),
                    ReversedByVoucherId = table.Column<int>(type: "integer", nullable: true),
                    ReversalOfVoucherId = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_Vouchers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRequests",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ExpenseAccountId = table.Column<int>(type: "integer", nullable: true),
                    PaidFromAccountId = table.Column<int>(type: "integer", nullable: true),
                    RequestedByUserId = table.Column<string>(type: "text", nullable: false),
                    RequestedByName = table.Column<string>(type: "text", nullable: false),
                    DepartmentId = table.Column<string>(type: "text", nullable: true),
                    PayeeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NeededBy = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ApprovalRequestId = table.Column<int>(type: "integer", nullable: true),
                    VoucherId = table.Column<int>(type: "integer", nullable: true),
                    SubmittedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_PaymentRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRequests_Accounts_ExpenseAccountId",
                        column: x => x.ExpenseAccountId,
                        principalSchema: "finance",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentRequests_Accounts_PaidFromAccountId",
                        column: x => x.PaidFromAccountId,
                        principalSchema: "finance",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentRequests_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalSchema: "finance",
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VoucherLines",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VoucherId = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<int>(type: "integer", nullable: false),
                    AccountCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Debit = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Narration = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PersonId = table.Column<string>(type: "text", nullable: true),
                    PersonName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoucherLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "finance",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherLines_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalSchema: "finance",
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Code",
                schema: "finance",
                table: "Accounts",
                column: "Code",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_IsDeleted",
                schema: "finance",
                table: "Accounts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ParentId",
                schema: "finance",
                table: "Accounts",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_document_sequences_Key_Year",
                schema: "finance",
                table: "document_sequences",
                columns: new[] { "Key", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalYears_IsDeleted",
                schema: "finance",
                table: "FiscalYears",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalYears_StartDate_EndDate",
                schema: "finance",
                table: "FiscalYears",
                columns: new[] { "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_DispatchedUtc_OccurredUtc",
                schema: "finance",
                table: "outbox_messages",
                columns: new[] { "DispatchedUtc", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_ExpenseAccountId",
                schema: "finance",
                table: "PaymentRequests",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_IsDeleted",
                schema: "finance",
                table: "PaymentRequests",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_PaidFromAccountId",
                schema: "finance",
                table: "PaymentRequests",
                column: "PaidFromAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_Reference",
                schema: "finance",
                table: "PaymentRequests",
                column: "Reference",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_Status",
                schema: "finance",
                table: "PaymentRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_VoucherId",
                schema: "finance",
                table: "PaymentRequests",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_AccountId",
                schema: "finance",
                table: "VoucherLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_PersonId",
                schema: "finance",
                table: "VoucherLines",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_VoucherId",
                schema: "finance",
                table: "VoucherLines",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_Date_Status",
                schema: "finance",
                table: "Vouchers",
                columns: new[] { "Date", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_IsDeleted",
                schema: "finance",
                table: "Vouchers",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_Number",
                schema: "finance",
                table: "Vouchers",
                column: "Number",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_SourceModule_SourceDocumentType_SourceDocumentId",
                schema: "finance",
                table: "Vouchers",
                columns: new[] { "SourceModule", "SourceDocumentType", "SourceDocumentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_sequences",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "FiscalYears",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "PaymentRequests",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "VoucherLines",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Accounts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Vouchers",
                schema: "finance");
        }
    }
}
