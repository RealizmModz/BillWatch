using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillWatch.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_BankAccounts_Id_UserId",
                table: "BankAccounts",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateTable(
                name: "BankTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaidTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    MerchantName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsoCurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    PostedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AuthorizedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsPending = table.Column<bool>(type: "boolean", nullable: false),
                    IsRemoved = table.Column<bool>(type: "boolean", nullable: false),
                    CategoryPrimary = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CategoryDetailed = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTransactions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankTransactions_BankAccounts_BankAccountId_UserId",
                        columns: x => new { x.BankAccountId, x.UserId },
                        principalTable: "BankAccounts",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_BankAccountId",
                table: "BankTransactions",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_BankAccountId_UserId",
                table: "BankTransactions",
                columns: new[] { "BankAccountId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_PostedDate",
                table: "BankTransactions",
                column: "PostedDate");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_UserId",
                table: "BankTransactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_UserId_PlaidTransactionId",
                table: "BankTransactions",
                columns: new[] { "UserId", "PlaidTransactionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankTransactions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BankAccounts_Id_UserId",
                table: "BankAccounts");
        }
    }
}
