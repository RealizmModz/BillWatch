using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillWatch.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillStreamId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatementId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentStatementId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Confidence = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PreviousAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountDifference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AnnualizedImpact = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsAcknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillChanges_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillChanges_BillStatements_CurrentStatementId_UserId",
                        columns: x => new { x.CurrentStatementId, x.UserId },
                        principalTable: "BillStatements",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillChanges_BillStatements_PreviousStatementId_UserId",
                        columns: x => new { x.PreviousStatementId, x.UserId },
                        principalTable: "BillStatements",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillChanges_BillStreams_BillStreamId_UserId",
                        columns: x => new { x.BillStreamId, x.UserId },
                        principalTable: "BillStreams",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillChanges_BillStreamId",
                table: "BillChanges",
                column: "BillStreamId");

            migrationBuilder.CreateIndex(
                name: "IX_BillChanges_BillStreamId_UserId",
                table: "BillChanges",
                columns: new[] { "BillStreamId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillChanges_CurrentStatementId",
                table: "BillChanges",
                column: "CurrentStatementId");

            migrationBuilder.CreateIndex(
                name: "IX_BillChanges_CurrentStatementId_UserId",
                table: "BillChanges",
                columns: new[] { "CurrentStatementId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillChanges_PreviousStatementId",
                table: "BillChanges",
                column: "PreviousStatementId");

            migrationBuilder.CreateIndex(
                name: "IX_BillChanges_PreviousStatementId_UserId",
                table: "BillChanges",
                columns: new[] { "PreviousStatementId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillChanges_UserId",
                table: "BillChanges",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BillChanges_UserId_IsAcknowledged_DetectedAtUtc",
                table: "BillChanges",
                columns: new[] { "UserId", "IsAcknowledged", "DetectedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillChanges");
        }
    }
}
