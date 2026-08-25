using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillWatch.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillStatements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillStatements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillStreamId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    StatementDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ProviderStatementId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RetrievedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillStatements_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillStatements_BillStreams_BillStreamId_UserId",
                        columns: x => new { x.BillStreamId, x.UserId },
                        principalTable: "BillStreams",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillStatements_BillStreamId",
                table: "BillStatements",
                column: "BillStreamId");

            migrationBuilder.CreateIndex(
                name: "IX_BillStatements_BillStreamId_UserId",
                table: "BillStatements",
                columns: new[] { "BillStreamId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillStatements_UserId",
                table: "BillStatements",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BillStatements_UserId_BillStreamId_PeriodStart_PeriodEnd",
                table: "BillStatements",
                columns: new[] { "UserId", "BillStreamId", "PeriodStart", "PeriodEnd" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillStatements");
        }
    }
}
