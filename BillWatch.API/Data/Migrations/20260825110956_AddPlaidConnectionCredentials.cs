using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillWatch.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaidConnectionCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProtectedPlaidAccessToken",
                table: "BankConnections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransactionsCursor",
                table: "BankConnections",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProtectedPlaidAccessToken",
                table: "BankConnections");

            migrationBuilder.DropColumn(
                name: "TransactionsCursor",
                table: "BankConnections");
        }
    }
}
