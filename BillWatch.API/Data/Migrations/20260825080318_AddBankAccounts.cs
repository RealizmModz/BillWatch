using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillWatch.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_BankConnections_Id_UserId",
                table: "BankConnections",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateTable(
                name: "BankAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BankConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaidAccountId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OfficialName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Mask = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    AccountType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AccountSubtype = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankAccounts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankAccounts_BankConnections_BankConnectionId_UserId",
                        columns: x => new { x.BankConnectionId, x.UserId },
                        principalTable: "BankConnections",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_BankConnectionId",
                table: "BankAccounts",
                column: "BankConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_BankConnectionId_UserId",
                table: "BankAccounts",
                columns: new[] { "BankConnectionId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_UserId",
                table: "BankAccounts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_UserId_PlaidAccountId",
                table: "BankAccounts",
                columns: new[] { "UserId", "PlaidAccountId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankAccounts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BankConnections_Id_UserId",
                table: "BankConnections");
        }
    }
}
