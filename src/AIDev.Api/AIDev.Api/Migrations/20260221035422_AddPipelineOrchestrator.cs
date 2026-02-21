using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIDev.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineOrchestrator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BranchDeleted",
                table: "DevRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CopilotBranchName",
                table: "DevRequests",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeployedAt",
                table: "DevRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeploymentRunId",
                table: "DevRequests",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeploymentStatus",
                table: "DevRequests",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "StallNotifiedAt",
                table: "DevRequests",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BranchDeleted",
                table: "DevRequests");

            migrationBuilder.DropColumn(
                name: "CopilotBranchName",
                table: "DevRequests");

            migrationBuilder.DropColumn(
                name: "DeployedAt",
                table: "DevRequests");

            migrationBuilder.DropColumn(
                name: "DeploymentRunId",
                table: "DevRequests");

            migrationBuilder.DropColumn(
                name: "DeploymentStatus",
                table: "DevRequests");

            migrationBuilder.DropColumn(
                name: "StallNotifiedAt",
                table: "DevRequests");
        }
    }
}
