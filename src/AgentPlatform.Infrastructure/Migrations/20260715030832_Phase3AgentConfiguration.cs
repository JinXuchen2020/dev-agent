using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable IDE0161 // File-scoped namespace - auto-generated

namespace AgentPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase3AgentConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    YamlContent = table.Column<string>(type: "text", nullable: false),
                    VersionMajor = table.Column<int>(type: "integer", nullable: false),
                    VersionMinor = table.Column<int>(type: "integer", nullable: false),
                    VersionPatch = table.Column<int>(type: "integer", nullable: false),
                    VersionChangeLog = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AgentTypeCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentConfigurations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentConfigurations_TenantId",
                table: "AgentConfigurations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentConfigurations_AgentTypeCode",
                table: "AgentConfigurations",
                column: "AgentTypeCode");

            migrationBuilder.CreateIndex(
                name: "IX_AgentConfigurations_TenantId_Status",
                table: "AgentConfigurations",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentConfigurations");
        }
    }
}
