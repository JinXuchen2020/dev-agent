using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable IDE0161 // File-scoped namespace - auto-generated

namespace AgentPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase5ApiKeyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_ExpiresAt",
                table: "ApiKeys");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_IsActive_RevokedAt_ExpiresAt",
                table: "ApiKeys",
                columns: new[] { "IsActive", "RevokedAt", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_IsActive_RevokedAt_ExpiresAt",
                table: "ApiKeys");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_ExpiresAt",
                table: "ApiKeys",
                column: "ExpiresAt");
        }
    }
}
