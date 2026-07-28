using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable IDE0161 // File-scoped namespace - auto-generated
#nullable disable

namespace AgentPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleCredentialsPerTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantCredentialSettings_TenantId_Category",
                table: "TenantCredentialSettings");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "TenantCredentialSettings",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_TenantCredentialSettings_TenantId_Category",
                table: "TenantCredentialSettings",
                columns: new[] { "TenantId", "Category" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantCredentialSettings_TenantId_Category",
                table: "TenantCredentialSettings");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "TenantCredentialSettings");

            migrationBuilder.CreateIndex(
                name: "IX_TenantCredentialSettings_TenantId_Category",
                table: "TenantCredentialSettings",
                columns: new[] { "TenantId", "Category" },
                unique: true);
        }
    }
}
