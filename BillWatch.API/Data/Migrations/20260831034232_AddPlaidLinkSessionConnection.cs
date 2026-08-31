using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillWatch.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaidLinkSessionConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BankConnectionId",
                table: "PlaidLinkSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaidLinkSessions_BankConnectionId_UserId",
                table: "PlaidLinkSessions",
                columns: new[] { "BankConnectionId", "UserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PlaidLinkSessions_BankConnections_BankConnectionId_UserId",
                table: "PlaidLinkSessions",
                columns: new[] { "BankConnectionId", "UserId" },
                principalTable: "BankConnections",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlaidLinkSessions_BankConnections_BankConnectionId_UserId",
                table: "PlaidLinkSessions");

            migrationBuilder.DropIndex(
                name: "IX_PlaidLinkSessions_BankConnectionId_UserId",
                table: "PlaidLinkSessions");

            migrationBuilder.DropColumn(
                name: "BankConnectionId",
                table: "PlaidLinkSessions");
        }
    }
}
