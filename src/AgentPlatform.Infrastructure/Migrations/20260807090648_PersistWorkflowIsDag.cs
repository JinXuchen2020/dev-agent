using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable IDE0161 // File-scoped namespace - auto-generated

namespace AgentPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PersistWorkflowIsDag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDag",
                table: "Workflows",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDag",
                table: "Workflows");
        }
    }
}
