using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable IDE0161 // 由 dotnet-ef 生成的迁移文件采用 block-scoped namespace，本项目强制 file-scoped，此处局部豁免

namespace AgentPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationAgentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgentId",
                table: "Conversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_TenantId_WorkflowId_AgentId",
                table: "Conversations",
                columns: new[] { "TenantId", "WorkflowId", "AgentId" },
                unique: true,
                filter: "\"AgentId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Conversations_TenantId_WorkflowId_AgentId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_AgentId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "Conversations");
        }
    }
}
