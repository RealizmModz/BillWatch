using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillWatch.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillStatementAiEvaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillStatementAiEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillStatementUploadId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CandidateReadyForValidation = table.Column<bool>(type: "boolean", nullable: false),
                    LastAttemptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillStatementAiEvaluations", x => x.Id);
                    table.UniqueConstraint("AK_BillStatementAiEvaluations_Id_UserId", x => new { x.Id, x.UserId });
                    table.CheckConstraint("CK_BillStatementAiEvaluations_AttemptCount", "\"AttemptCount\" >= 0 AND \"AttemptCount\" <= 1");
                    table.ForeignKey(
                        name: "FK_BillStatementAiEvaluations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillStatementAiEvaluations_BillStatementUploads_BillStateme~",
                        columns: x => new { x.BillStatementUploadId, x.UserId },
                        principalTable: "BillStatementUploads",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillStatementAiEvaluations_BillStatementUploadId_UserId",
                table: "BillStatementAiEvaluations",
                columns: new[] { "BillStatementUploadId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillStatementAiEvaluations_UserId",
                table: "BillStatementAiEvaluations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BillStatementAiEvaluations_UserId_BillStatementUploadId_Pro~",
                table: "BillStatementAiEvaluations",
                columns: new[] { "UserId", "BillStatementUploadId", "Provider", "Model", "PromptVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillStatementAiEvaluations_UserId_Status_CreatedAtUtc",
                table: "BillStatementAiEvaluations",
                columns: new[] { "UserId", "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillStatementAiEvaluations");
        }
    }
}
