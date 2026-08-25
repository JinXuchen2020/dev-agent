using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable IDE0161 // Convert to file-scoped namespace
#nullable disable

namespace AgentPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableExecutionCheckpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckpointData",
                table: "ExecutionLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CheckpointVersion",
                table: "ExecutionLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckpointData",
                table: "ExecutionLogs");

            migrationBuilder.DropColumn(
                name: "CheckpointVersion",
                table: "ExecutionLogs");
        }
    }
}
