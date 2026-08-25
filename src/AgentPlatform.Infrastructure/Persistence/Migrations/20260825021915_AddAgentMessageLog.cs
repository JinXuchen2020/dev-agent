using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable IDE0161 // Convert to file-scoped namespace
#nullable disable

namespace AgentPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentMessageLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "BlackboardSnapshot",
                table: "RunningExecutions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CheckpointData",
                table: "ExecutionLogs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "AgentMessageLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SenderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceiverId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageType = table.Column<int>(type: "INTEGER", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Round = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentMessageLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentMessageLogs_CorrelationId",
                table: "AgentMessageLogs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentMessageLogs_TenantId_WorkflowId",
                table: "AgentMessageLogs",
                columns: new[] { "TenantId", "WorkflowId" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentMessageLogs_WorkflowId_ConsumedAt",
                table: "AgentMessageLogs",
                columns: new[] { "WorkflowId", "ConsumedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentMessageLogs");

            migrationBuilder.AlterColumn<string>(
                name: "BlackboardSnapshot",
                table: "RunningExecutions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CheckpointData",
                table: "ExecutionLogs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}

