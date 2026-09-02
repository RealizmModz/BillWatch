using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BillWatch.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminSubscriptionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminAuditLogs_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdminAuditLogs_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionAccessKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    DisplayPrefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Tier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    GrantsLifetimeAccess = table.Column<bool>(type: "boolean", nullable: false),
                    MaxRedemptions = table.Column<int>(type: "integer", nullable: false),
                    RedemptionCount = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionAccessKeys", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionAccessKeys_GrantDuration", "(\"GrantsLifetimeAccess\" = TRUE AND \"DurationDays\" IS NULL) OR (\"GrantsLifetimeAccess\" = FALSE AND \"DurationDays\" IS NOT NULL AND \"DurationDays\" > 0)");
                    table.CheckConstraint("CK_SubscriptionAccessKeys_Redemptions", "\"MaxRedemptions\" > 0 AND \"RedemptionCount\" >= 0 AND \"RedemptionCount\" <= \"MaxRedemptions\"");
                    table.CheckConstraint("CK_SubscriptionAccessKeys_Revocation", "(\"IsRevoked\" = FALSE AND \"RevokedAtUtc\" IS NULL) OR (\"IsRevoked\" = TRUE AND \"RevokedAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_SubscriptionAccessKeys_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionEntitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionEntitlements", x => x.Id);
                    table.UniqueConstraint("AK_SubscriptionEntitlements_Id_UserId", x => new { x.Id, x.UserId });
                    table.CheckConstraint("CK_SubscriptionEntitlements_Period", "\"EndsAtUtc\" IS NULL OR \"EndsAtUtc\" > \"StartsAtUtc\"");
                    table.CheckConstraint("CK_SubscriptionEntitlements_Revocation", "(\"IsRevoked\" = FALSE AND \"RevokedAtUtc\" IS NULL) OR (\"IsRevoked\" = TRUE AND \"RevokedAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_SubscriptionEntitlements_AspNetUsers_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SubscriptionEntitlements_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserProgramMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Program = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProgramMemberships", x => x.Id);
                    table.UniqueConstraint("AK_UserProgramMemberships_Id_UserId", x => new { x.Id, x.UserId });
                    table.CheckConstraint("CK_UserProgramMemberships_Period", "\"EndsAtUtc\" IS NULL OR \"EndsAtUtc\" > \"StartsAtUtc\"");
                    table.ForeignKey(
                        name: "FK_UserProgramMemberships_AspNetUsers_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserProgramMemberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionAccessKeyRedemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntitlementId = table.Column<Guid>(type: "uuid", nullable: false),
                    RedeemedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionAccessKeyRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionAccessKeyRedemptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubscriptionAccessKeyRedemptions_SubscriptionAccessKeys_Acc~",
                        column: x => x.AccessKeyId,
                        principalTable: "SubscriptionAccessKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionAccessKeyRedemptions_SubscriptionEntitlements_E~",
                        columns: x => new { x.EntitlementId, x.UserId },
                        principalTable: "SubscriptionEntitlements",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { new Guid("0f112ee4-1690-4a08-925f-e72721626f51"), "0f112ee4-1690-4a08-925f-e72721626f51", "Owner", "OWNER" },
                    { new Guid("64cdd793-8aac-4f3b-814f-aa0272a02f28"), "64cdd793-8aac-4f3b-814f-aa0272a02f28", "Moderator", "MODERATOR" },
                    { new Guid("db2a4b76-a5a7-4c60-a66e-44630f39ed93"), "db2a4b76-a5a7-4c60-a66e-44630f39ed93", "Admin", "ADMIN" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_ActorUserId",
                table: "AdminAuditLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_CreatedAtUtc",
                table: "AdminAuditLogs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_SubjectType_SubjectId",
                table: "AdminAuditLogs",
                columns: new[] { "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_TargetUserId",
                table: "AdminAuditLogs",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAccessKeyRedemptions_AccessKeyId_UserId",
                table: "SubscriptionAccessKeyRedemptions",
                columns: new[] { "AccessKeyId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAccessKeyRedemptions_EntitlementId",
                table: "SubscriptionAccessKeyRedemptions",
                column: "EntitlementId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAccessKeyRedemptions_EntitlementId_UserId",
                table: "SubscriptionAccessKeyRedemptions",
                columns: new[] { "EntitlementId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAccessKeyRedemptions_UserId",
                table: "SubscriptionAccessKeyRedemptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAccessKeys_CreatedByUserId",
                table: "SubscriptionAccessKeys",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAccessKeys_IsRevoked_ExpiresAtUtc",
                table: "SubscriptionAccessKeys",
                columns: new[] { "IsRevoked", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAccessKeys_KeyHash",
                table: "SubscriptionAccessKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEntitlements_GrantedByUserId",
                table: "SubscriptionEntitlements",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEntitlements_UserId",
                table: "SubscriptionEntitlements",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEntitlements_UserId_IsRevoked_StartsAtUtc_EndsA~",
                table: "SubscriptionEntitlements",
                columns: new[] { "UserId", "IsRevoked", "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserProgramMemberships_GrantedByUserId",
                table: "UserProgramMemberships",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProgramMemberships_UserId_IsActive_EndsAtUtc",
                table: "UserProgramMemberships",
                columns: new[] { "UserId", "IsActive", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserProgramMemberships_UserId_Program",
                table: "UserProgramMemberships",
                columns: new[] { "UserId", "Program" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminAuditLogs");

            migrationBuilder.DropTable(
                name: "SubscriptionAccessKeyRedemptions");

            migrationBuilder.DropTable(
                name: "UserProgramMemberships");

            migrationBuilder.DropTable(
                name: "SubscriptionAccessKeys");

            migrationBuilder.DropTable(
                name: "SubscriptionEntitlements");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("0f112ee4-1690-4a08-925f-e72721626f51"));

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("64cdd793-8aac-4f3b-814f-aa0272a02f28"));

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("db2a4b76-a5a7-4c60-a66e-44630f39ed93"));
        }
    }
}
