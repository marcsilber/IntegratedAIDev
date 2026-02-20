using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIDev.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create Projects table first
            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GitHubOwner = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    GitHubRepo = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_GitHubOwner_GitHubRepo",
                table: "Projects",
                columns: new[] { "GitHubOwner", "GitHubRepo" },
                unique: true);

            // 2. Seed a default project for existing requests
            migrationBuilder.Sql(
                "INSERT INTO Projects (GitHubOwner, GitHubRepo, DisplayName, Description, FullName, IsActive, LastSyncedAt, CreatedAt) " +
                "VALUES ('marcsilber', 'IntegratedAIDev', 'IntegratedAIDev', 'AI-assisted development pipeline', 'marcsilber/IntegratedAIDev', 1, datetime('now'), datetime('now'))");

            // 3. Add ProjectId column with default pointing to the seeded project
            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "DevRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // 4. Point all existing requests at the default project
            migrationBuilder.Sql("UPDATE DevRequests SET ProjectId = (SELECT Id FROM Projects WHERE GitHubRepo = 'IntegratedAIDev' LIMIT 1)");

            // 5. Add FK and index
            migrationBuilder.CreateIndex(
                name: "IX_DevRequests_ProjectId",
                table: "DevRequests",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_DevRequests_Projects_ProjectId",
                table: "DevRequests",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DevRequests_Projects_ProjectId",
                table: "DevRequests");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_DevRequests_ProjectId",
                table: "DevRequests");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "DevRequests");
        }
    }
}
