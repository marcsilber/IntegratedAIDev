using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIDev.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProductOwnerAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AgentReviewId",
                table: "RequestComments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAgentComment",
                table: "RequestComments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AgentReviewCount",
                table: "DevRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAgentReviewAt",
                table: "DevRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DevRequestId = table.Column<int>(type: "INTEGER", nullable: false),
                    AgentType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Decision = table.Column<string>(type: "TEXT", nullable: false),
                    Reasoning = table.Column<string>(type: "TEXT", nullable: false),
                    AlignmentScore = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletenessScore = table.Column<int>(type: "INTEGER", nullable: false),
                    SalesAlignmentScore = table.Column<int>(type: "INTEGER", nullable: false),
                    SuggestedPriority = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: true),
                    PromptTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletionTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelUsed = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentReviews_DevRequests_DevRequestId",
                        column: x => x.DevRequestId,
                        principalTable: "DevRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestComments_AgentReviewId",
                table: "RequestComments",
                column: "AgentReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentReviews_DevRequestId",
                table: "AgentReviews",
                column: "DevRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestComments_AgentReviews_AgentReviewId",
                table: "RequestComments",
                column: "AgentReviewId",
                principalTable: "AgentReviews",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RequestComments_AgentReviews_AgentReviewId",
                table: "RequestComments");

            migrationBuilder.DropTable(
                name: "AgentReviews");

            migrationBuilder.DropIndex(
                name: "IX_RequestComments_AgentReviewId",
                table: "RequestComments");

            migrationBuilder.DropColumn(
                name: "AgentReviewId",
                table: "RequestComments");

            migrationBuilder.DropColumn(
                name: "IsAgentComment",
                table: "RequestComments");

            migrationBuilder.DropColumn(
                name: "AgentReviewCount",
                table: "DevRequests");

            migrationBuilder.DropColumn(
                name: "LastAgentReviewAt",
                table: "DevRequests");
        }
    }
}
