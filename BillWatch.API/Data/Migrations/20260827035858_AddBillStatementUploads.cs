using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillWatch.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillStatementUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillStatementUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillStreamId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillStatementId = table.Column<Guid>(type: "uuid", nullable: true),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileExtension = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillStatementUploads", x => x.Id);
                    table.UniqueConstraint("AK_BillStatementUploads_Id_UserId", x => new { x.Id, x.UserId });
                    table.ForeignKey(
                        name: "FK_BillStatementUploads_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillStatementUploads_BillStatements_BillStatementId_UserId",
                        columns: x => new { x.BillStatementId, x.UserId },
                        principalTable: "BillStatements",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillStatementUploads_BillStreams_BillStreamId_UserId",
                        columns: x => new { x.BillStreamId, x.UserId },
                        principalTable: "BillStreams",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillStatementUploads_BillStatementId",
                table: "BillStatementUploads",
                column: "BillStatementId");

            migrationBuilder.CreateIndex(
                name: "IX_BillStatementUploads_BillStatementId_UserId",
                table: "BillStatementUploads",
                columns: new[] { "BillStatementId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillStatementUploads_BillStreamId",
                table: "BillStatementUploads",
                column: "BillStreamId");

            migrationBuilder.CreateIndex(
                name: "IX_BillStatementUploads_BillStreamId_UserId",
                table: "BillStatementUploads",
                columns: new[] { "BillStreamId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillStatementUploads_UserId",
                table: "BillStatementUploads",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BillStatementUploads_UserId_Status_CreatedAtUtc",
                table: "BillStatementUploads",
                columns: new[] { "UserId", "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillStatementUploads");
        }
    }
}
