using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable IDE0161 // File-scoped namespace - auto-generated

namespace AgentPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentRunHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentRunRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Goal = table.Column<string>(type: "text", nullable: false),
                    FinalAnswer = table.Column<string>(type: "text", nullable: true),
                    Iterations = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalTokensIn = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalTokensOut = table.Column<int>(type: "INTEGER", nullable: false),
                    ArtifactCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRunRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunRecords_RunId",
                table: "AgentRunRecords",
                column: "RunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunRecords_TenantId",
                table: "AgentRunRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunRecords_TenantId_AgentId_CreatedAt",
                table: "AgentRunRecords",
                columns: new[] { "TenantId", "AgentId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentRunRecords");
        }
    }
}
