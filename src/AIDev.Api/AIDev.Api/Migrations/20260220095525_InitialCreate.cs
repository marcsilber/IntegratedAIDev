using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIDev.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DevRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    RequestType = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<string>(type: "TEXT", nullable: false),
                    StepsToReproduce = table.Column<string>(type: "TEXT", nullable: true),
                    ExpectedBehavior = table.Column<string>(type: "TEXT", nullable: true),
                    ActualBehavior = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    SubmittedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SubmittedByEmail = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    GitHubIssueNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    GitHubIssueUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequestComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DevRequestId = table.Column<int>(type: "INTEGER", nullable: false),
                    Author = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestComments_DevRequests_DevRequestId",
                        column: x => x.DevRequestId,
                        principalTable: "DevRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestComments_DevRequestId",
                table: "RequestComments",
                column: "DevRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestComments");

            migrationBuilder.DropTable(
                name: "DevRequests");
        }
    }
}
