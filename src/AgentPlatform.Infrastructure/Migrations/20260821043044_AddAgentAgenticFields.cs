using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable IDE0161 // File-scoped namespace - auto-generated

namespace AgentPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentAgenticFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedToolNamesJson",
                table: "Agents",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "MaxIterations",
                table: "Agents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 25);

            migrationBuilder.AddColumn<string>(
                name: "StopCriteria",
                table: "Agents",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedToolNamesJson",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "MaxIterations",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "StopCriteria",
                table: "Agents");
        }
    }
}
