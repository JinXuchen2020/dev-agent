using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable IDE0161 // 由 dotnet-ef 生成的迁移文件采用 block-scoped namespace，本项目强制 file-scoped，此处局部豁免

namespace AgentPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentRoleIsBuiltIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBuiltIn",
                table: "AgentRoleDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBuiltIn",
                table: "AgentRoleDefinitions");
        }
    }
}
