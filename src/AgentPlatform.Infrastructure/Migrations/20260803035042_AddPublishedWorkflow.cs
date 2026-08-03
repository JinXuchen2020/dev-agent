using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable IDE0161 // 由 dotnet-ef 生成的迁移文件采用 block-scoped namespace，本项目强制 file-scoped，此处局部豁免
#nullable disable

namespace AgentPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishedWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublishedWorkflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InputSchemaJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishedWorkflows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublishedWorkflows_TenantId_Slug",
                table: "PublishedWorkflows",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublishedWorkflows_TenantId_WorkflowId",
                table: "PublishedWorkflows",
                columns: new[] { "TenantId", "WorkflowId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublishedWorkflows");
        }
    }
}
