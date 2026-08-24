using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable IDE0161 // Convert to file-scoped namespace
#nullable disable

namespace AgentPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunningExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunningExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowState = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    HeartbeatAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LeaseExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CheckpointVersion = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    BlackboardSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunningExecutions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunningExecutions_TenantId_WorkflowState_LeaseExpiresAt",
                table: "RunningExecutions",
                columns: new[] { "TenantId", "WorkflowState", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RunningExecutions_WorkflowId",
                table: "RunningExecutions",
                column: "WorkflowId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunningExecutions");
        }
    }
}
