using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIDev.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CodeReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DevRequestId = table.Column<int>(type: "INTEGER", nullable: false),
                    PrNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Decision = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    DesignCompliance = table.Column<bool>(type: "INTEGER", nullable: false),
                    DesignComplianceNotes = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityPass = table.Column<bool>(type: "INTEGER", nullable: false),
                    SecurityNotes = table.Column<string>(type: "TEXT", nullable: true),
                    CodingStandardsPass = table.Column<bool>(type: "INTEGER", nullable: false),
                    CodingStandardsNotes = table.Column<string>(type: "TEXT", nullable: true),
                    QualityScore = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesChanged = table.Column<int>(type: "INTEGER", nullable: false),
                    LinesAdded = table.Column<int>(type: "INTEGER", nullable: false),
                    LinesRemoved = table.Column<int>(type: "INTEGER", nullable: false),
                    PromptTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletionTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelUsed = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeReviews_DevRequests_DevRequestId",
                        column: x => x.DevRequestId,
                        principalTable: "DevRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodeReviews_DevRequestId",
                table: "CodeReviews",
                column: "DevRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodeReviews");
        }
    }
}
