using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable IDE0161 // File-scoped namespace - auto-generated

namespace AgentPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvaluationDatasets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationDatasets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Input = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ExpectedOutput = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    MatchMode = table.Column<int>(type: "INTEGER", nullable: false),
                    EvaluationDatasetId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationCases_EvaluationDatasets_EvaluationDatasetId",
                        column: x => x.EvaluationDatasetId,
                        principalTable: "EvaluationDatasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationCases_EvaluationDatasetId",
                table: "EvaluationCases",
                column: "EvaluationDatasetId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationDatasets_TenantId_Name",
                table: "EvaluationDatasets",
                columns: new[] { "TenantId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvaluationCases");

            migrationBuilder.DropTable(
                name: "EvaluationDatasets");
        }
    }
}
