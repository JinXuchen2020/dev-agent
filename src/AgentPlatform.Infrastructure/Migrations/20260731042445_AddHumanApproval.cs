using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable IDE0161 // 由 dotnet-ef 生成的迁移文件采用 block-scoped namespace，本项目强制 file-scoped，此处局部豁免
#nullable disable

namespace AgentPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHumanApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HumanApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SubmittedInput = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HumanApprovals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HumanApprovals_TenantId_WorkflowId_NodeName_Status",
                table: "HumanApprovals",
                columns: new[] { "TenantId", "WorkflowId", "NodeName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HumanApprovals_WorkflowId",
                table: "HumanApprovals",
                column: "WorkflowId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HumanApprovals");
        }
    }
}
