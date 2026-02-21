using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIDev.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentRetryCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeploymentRetryCount",
                table: "DevRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeploymentRetryCount",
                table: "DevRequests");
        }
    }
}
