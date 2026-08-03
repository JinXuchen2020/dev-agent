using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable IDE0161 // 由 dotnet-ef 生成的迁移文件采用 block-scoped namespace，本项目强制 file-scoped，此处局部豁免
#nullable disable

namespace AgentPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowTriggersAndBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationWorkflowBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationWorkflowBindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTriggers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TriggerToken = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Cron = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Timezone = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTriggers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationWorkflowBindings_TenantId_ConversationId",
                table: "ConversationWorkflowBindings",
                columns: new[] { "TenantId", "ConversationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationWorkflowBindings_TenantId_WorkflowId",
                table: "ConversationWorkflowBindings",
                columns: new[] { "TenantId", "WorkflowId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTriggers_NextRunAt",
                table: "WorkflowTriggers",
                column: "NextRunAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTriggers_TenantId_WorkflowId_Type",
                table: "WorkflowTriggers",
                columns: new[] { "TenantId", "WorkflowId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTriggers_TriggerToken",
                table: "WorkflowTriggers",
                column: "TriggerToken");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationWorkflowBindings");

            migrationBuilder.DropTable(
                name: "WorkflowTriggers");
        }
    }
}
