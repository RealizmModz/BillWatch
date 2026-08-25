using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillWatch.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_BillChanges_Id_UserId",
                table: "BillChanges",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateTable(
                name: "BillAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillStreamId = table.Column<Guid>(type: "uuid", nullable: true),
                    BillChangeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlertType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    IsDismissed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillAlerts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillAlerts_BillChanges_BillChangeId_UserId",
                        columns: x => new { x.BillChangeId, x.UserId },
                        principalTable: "BillChanges",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillAlerts_BillStreams_BillStreamId_UserId",
                        columns: x => new { x.BillStreamId, x.UserId },
                        principalTable: "BillStreams",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillAlerts_BillChangeId",
                table: "BillAlerts",
                column: "BillChangeId");

            migrationBuilder.CreateIndex(
                name: "IX_BillAlerts_BillChangeId_UserId",
                table: "BillAlerts",
                columns: new[] { "BillChangeId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillAlerts_BillStreamId",
                table: "BillAlerts",
                column: "BillStreamId");

            migrationBuilder.CreateIndex(
                name: "IX_BillAlerts_BillStreamId_UserId",
                table: "BillAlerts",
                columns: new[] { "BillStreamId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillAlerts_UserId",
                table: "BillAlerts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BillAlerts_UserId_IsDismissed_IsRead_CreatedAtUtc",
                table: "BillAlerts",
                columns: new[] { "UserId", "IsDismissed", "IsRead", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillAlerts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BillChanges_Id_UserId",
                table: "BillChanges");
        }
    }
}
