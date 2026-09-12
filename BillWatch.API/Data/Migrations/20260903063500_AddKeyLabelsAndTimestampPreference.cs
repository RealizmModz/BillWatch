using Microsoft.EntityFrameworkCore.Migrations;

namespace BillWatch.API.Data.Migrations;

public partial class AddKeyLabelsAndTimestampPreference : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "TimestampDisplayMode",
            table: "AspNetUsers",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "Label",
            table: "SubscriptionAccessKeys",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TimestampDisplayMode",
            table: "AspNetUsers");

        migrationBuilder.DropColumn(
            name: "Label",
            table: "SubscriptionAccessKeys");
    }
}
