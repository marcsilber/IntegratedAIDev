using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIDev.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddArchitectAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ArchitectReviewId",
                table: "RequestComments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ArchitectReviewCount",
                table: "DevRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastArchitectReviewAt",
                table: "DevRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArchitectReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DevRequestId = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SolutionSummary = table.Column<string>(type: "TEXT", nullable: false),
                    Approach = table.Column<string>(type: "TEXT", nullable: false),
                    SolutionJson = table.Column<string>(type: "TEXT", nullable: false),
                    EstimatedComplexity = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EstimatedEffort = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FilesAnalysed = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesReadJson = table.Column<string>(type: "TEXT", nullable: true),
                    Decision = table.Column<string>(type: "TEXT", nullable: false),
                    HumanFeedback = table.Column<string>(type: "TEXT", nullable: true),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Step1PromptTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    Step1CompletionTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    Step2PromptTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    Step2CompletionTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelUsed = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TotalDurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchitectReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchitectReviews_DevRequests_DevRequestId",
                        column: x => x.DevRequestId,
                        principalTable: "DevRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestComments_ArchitectReviewId",
                table: "RequestComments",
                column: "ArchitectReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchitectReviews_DevRequestId",
                table: "ArchitectReviews",
                column: "DevRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestComments_ArchitectReviews_ArchitectReviewId",
                table: "RequestComments",
                column: "ArchitectReviewId",
                principalTable: "ArchitectReviews",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RequestComments_ArchitectReviews_ArchitectReviewId",
                table: "RequestComments");

            migrationBuilder.DropTable(
                name: "ArchitectReviews");

            migrationBuilder.DropIndex(
                name: "IX_RequestComments_ArchitectReviewId",
                table: "RequestComments");

            migrationBuilder.DropColumn(
                name: "ArchitectReviewId",
                table: "RequestComments");

            migrationBuilder.DropColumn(
                name: "ArchitectReviewCount",
                table: "DevRequests");

            migrationBuilder.DropColumn(
                name: "LastArchitectReviewAt",
                table: "DevRequests");
        }
    }
}
