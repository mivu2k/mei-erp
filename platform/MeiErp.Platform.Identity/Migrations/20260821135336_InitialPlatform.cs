using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeiErp.Platform.Identity.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "ApprovalDelegations",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FromUserId = table.Column<string>(type: "text", nullable: false),
                    FromName = table.Column<string>(type: "text", nullable: false),
                    ToUserId = table.Column<string>(type: "text", nullable: false),
                    ToName = table.Column<string>(type: "text", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DocumentType = table.Column<string>(type: "text", nullable: true),
                    MaxAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    FromLeave = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_ApprovalDelegations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalRequests",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    ModuleKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DocumentId = table.Column<int>(type: "integer", nullable: false),
                    DocumentReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DocumentUrl = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    RequestedByUserId = table.Column<string>(type: "text", nullable: false),
                    RequestedByName = table.Column<string>(type: "text", nullable: false),
                    RequestedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DepartmentId = table.Column<string>(type: "text", nullable: true),
                    ProjectId = table.Column<int>(type: "integer", nullable: true),
                    WorkflowDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    DefinitionRevision = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentStepOrder = table.Column<int>(type: "integer", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_ApprovalRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ModuleKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsSystemRole = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompanyProfiles",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    LegalName = table.Column<string>(type: "text", nullable: true),
                    Logo = table.Column<byte[]>(type: "bytea", nullable: true),
                    AddressLine1 = table.Column<string>(type: "text", nullable: true),
                    AddressLine2 = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Website = table.Column<string>(type: "text", nullable: true),
                    TaxNumber = table.Column<string>(type: "text", nullable: true),
                    SalesTaxNumber = table.Column<string>(type: "text", nullable: true),
                    FooterNote = table.Column<string>(type: "text", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    CurrencySymbol = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workflows",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    BlockSelfApproval = table.Column<bool>(type: "boolean", nullable: false),
                    RequireDistinctApprovers = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_Workflows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalActions",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApprovalRequestId = table.Column<int>(type: "integer", nullable: false),
                    StepOrder = table.Column<int>(type: "integer", nullable: false),
                    StepName = table.Column<string>(type: "text", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    ActedByUserId = table.Column<string>(type: "text", nullable: false),
                    ActedByName = table.Column<string>(type: "text", nullable: false),
                    OnBehalfOfUserId = table.Column<string>(type: "text", nullable: true),
                    OnBehalfOfName = table.Column<string>(type: "text", nullable: true),
                    ActedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalActions_ApprovalRequests_ApprovalRequestId",
                        column: x => x.ApprovalRequestId,
                        principalSchema: "platform",
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalStepStates",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApprovalRequestId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Rule = table.Column<int>(type: "integer", nullable: false),
                    RuleValue = table.Column<string>(type: "text", nullable: true),
                    Quorum = table.Column<int>(type: "integer", nullable: false),
                    AllowReturn = table.Column<bool>(type: "boolean", nullable: false),
                    ReminderAfterHours = table.Column<int>(type: "integer", nullable: true),
                    EscalateAfterHours = table.Column<int>(type: "integer", nullable: true),
                    EscalateToRole = table.Column<string>(type: "text", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SettledUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemindedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EscalatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequiredApprovals = table.Column<int>(type: "integer", nullable: false),
                    ReceivedApprovals = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalStepStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalStepStates_ApprovalRequests_ApprovalRequestId",
                        column: x => x.ApprovalRequestId,
                        principalSchema: "platform",
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "platform",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowSteps",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkflowDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Rule = table.Column<int>(type: "integer", nullable: false),
                    RuleValue = table.Column<string>(type: "text", nullable: true),
                    Quorum = table.Column<int>(type: "integer", nullable: false),
                    MinAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    MaxAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    ReminderAfterHours = table.Column<int>(type: "integer", nullable: true),
                    EscalateAfterHours = table.Column<int>(type: "integer", nullable: true),
                    EscalateToRole = table.Column<string>(type: "text", nullable: true),
                    AllowReturn = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowSteps_Workflows_WorkflowDefinitionId",
                        column: x => x.WorkflowDefinitionId,
                        principalSchema: "platform",
                        principalTable: "Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                schema: "platform",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                schema: "platform",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "platform",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EmployeeCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Designation = table.Column<string>(type: "text", nullable: true),
                    DepartmentId = table.Column<string>(type: "text", nullable: true),
                    LineManagerId = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MustChangePassword = table.Column<bool>(type: "boolean", nullable: false),
                    Photo = table.Column<byte[]>(type: "bytea", nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_AspNetUsers_LineManagerId",
                        column: x => x.LineManagerId,
                        principalSchema: "platform",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                schema: "platform",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "platform",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "text", nullable: true),
                    HeadUserId = table.Column<string>(type: "text", nullable: true),
                    ParentId = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Departments_AspNetUsers_HeadUserId",
                        column: x => x.HeadUserId,
                        principalSchema: "platform",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Departments_Departments_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "platform",
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ModuleAccess",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ModuleKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Granted = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    SetUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SetBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModuleAccess", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModuleAccess_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "platform",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalActions_ActedByUserId",
                schema: "platform",
                table: "ApprovalActions",
                column: "ActedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalActions_ApprovalRequestId",
                schema: "platform",
                table: "ApprovalActions",
                column: "ApprovalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDelegations_FromUserId_FromDate_ToDate",
                schema: "platform",
                table: "ApprovalDelegations",
                columns: new[] { "FromUserId", "FromDate", "ToDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_DocumentType_DocumentId_Status",
                schema: "platform",
                table: "ApprovalRequests",
                columns: new[] { "DocumentType", "DocumentId", "Status" },
                unique: true,
                filter: "\"Status\" = 0 AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_Status_CurrentStepOrder",
                schema: "platform",
                table: "ApprovalRequests",
                columns: new[] { "Status", "CurrentStepOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalStepStates_ApprovalRequestId",
                schema: "platform",
                table: "ApprovalStepStates",
                column: "ApprovalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                schema: "platform",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoles_ModuleKey",
                schema: "platform",
                table: "AspNetRoles",
                column: "ModuleKey");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "platform",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                schema: "platform",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                schema: "platform",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                schema: "platform",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "platform",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DepartmentId",
                schema: "platform",
                table: "AspNetUsers",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_EmployeeCode",
                schema: "platform",
                table: "AspNetUsers",
                column: "EmployeeCode",
                unique: true,
                filter: "\"EmployeeCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_IsActive",
                schema: "platform",
                table: "AspNetUsers",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_LineManagerId",
                schema: "platform",
                table: "AspNetUsers",
                column: "LineManagerId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "platform",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_HeadUserId",
                schema: "platform",
                table: "Departments",
                column: "HeadUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_ParentId",
                schema: "platform",
                table: "Departments",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_ModuleAccess_UserId_ModuleKey",
                schema: "platform",
                table: "ModuleAccess",
                columns: new[] { "UserId", "ModuleKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_DocumentType_IsActive",
                schema: "platform",
                table: "Workflows",
                columns: new[] { "DocumentType", "IsActive" },
                unique: true,
                filter: "\"IsActive\" = true AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSteps_WorkflowDefinitionId",
                schema: "platform",
                table: "WorkflowSteps",
                column: "WorkflowDefinitionId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                schema: "platform",
                table: "AspNetUserClaims",
                column: "UserId",
                principalSchema: "platform",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                schema: "platform",
                table: "AspNetUserLogins",
                column: "UserId",
                principalSchema: "platform",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                schema: "platform",
                table: "AspNetUserRoles",
                column: "UserId",
                principalSchema: "platform",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Departments_DepartmentId",
                schema: "platform",
                table: "AspNetUsers",
                column: "DepartmentId",
                principalSchema: "platform",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Departments_AspNetUsers_HeadUserId",
                schema: "platform",
                table: "Departments");

            migrationBuilder.DropTable(
                name: "ApprovalActions",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "ApprovalDelegations",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "ApprovalStepStates",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "CompanyProfiles",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "ModuleAccess",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "WorkflowSteps",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "ApprovalRequests",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "AspNetRoles",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "Workflows",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "AspNetUsers",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "Departments",
                schema: "platform");
        }
    }
}
