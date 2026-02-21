using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIDev.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCopilotImplementation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CopilotCompletedAt",
                table: "DevRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CopilotPrNumber",
                table: "DevRequests",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CopilotPrUrl",
                table: "DevRequests",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CopilotSessionId",
                table: "DevRequests",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CopilotStatus",
                table: "DevRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CopilotTriggeredAt",
                table: "DevRequests",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CopilotCompletedAt",
                table: "DevRequests");

            migrationBuilder.DropColumn(
                name: "CopilotPrNumber",
                table: "DevRequests");

            migrationBuilder.DropColumn(
                name: "CopilotPrUrl",
                table: "DevRequests");

            migrationBuilder.DropColumn(
                name: "CopilotSessionId",
                table: "DevRequests");

            migrationBuilder.DropColumn(
                name: "CopilotStatus",
                table: "DevRequests");

            migrationBuilder.DropColumn(
                name: "CopilotTriggeredAt",
                table: "DevRequests");
        }
    }
}
