using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillWatch.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkTransactionsToBillStreams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BillStreamId",
                table: "BankTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_BillStreams_Id_UserId",
                table: "BillStreams",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_BillStreamId",
                table: "BankTransactions",
                column: "BillStreamId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_BillStreamId_UserId",
                table: "BankTransactions",
                columns: new[] { "BillStreamId", "UserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_BankTransactions_BillStreams_BillStreamId_UserId",
                table: "BankTransactions",
                columns: new[] { "BillStreamId", "UserId" },
                principalTable: "BillStreams",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankTransactions_BillStreams_BillStreamId_UserId",
                table: "BankTransactions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BillStreams_Id_UserId",
                table: "BillStreams");

            migrationBuilder.DropIndex(
                name: "IX_BankTransactions_BillStreamId",
                table: "BankTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BankTransactions_BillStreamId_UserId",
                table: "BankTransactions");

            migrationBuilder.DropColumn(
                name: "BillStreamId",
                table: "BankTransactions");
        }
    }
}
