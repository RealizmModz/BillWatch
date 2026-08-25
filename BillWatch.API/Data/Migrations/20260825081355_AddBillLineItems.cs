using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillWatch.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillLineItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_BillStatements_Id_UserId",
                table: "BillStatements",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateTable(
                name: "BillLineItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillStatementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillLineItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillLineItems_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillLineItems_BillStatements_BillStatementId_UserId",
                        columns: x => new { x.BillStatementId, x.UserId },
                        principalTable: "BillStatements",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillLineItems_BillStatementId",
                table: "BillLineItems",
                column: "BillStatementId");

            migrationBuilder.CreateIndex(
                name: "IX_BillLineItems_BillStatementId_SortOrder",
                table: "BillLineItems",
                columns: new[] { "BillStatementId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_BillLineItems_BillStatementId_UserId",
                table: "BillLineItems",
                columns: new[] { "BillStatementId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillLineItems_UserId",
                table: "BillLineItems",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillLineItems");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BillStatements_Id_UserId",
                table: "BillStatements");
        }
    }
}
