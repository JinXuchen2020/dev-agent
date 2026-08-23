using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable IDE0161 // File-scoped namespace - auto-generated

namespace AgentPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillAgentMaxIterationsZero : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 此前已存在的 agent 在创建时落库的 MaxIterations=25 是「硬上限」语义；
            // 新默认值为 0（无上限）。这里回填历史数据，使既有 agent 也遵循「无上限」语义，
            // 与代码/Domain 默认值对齐。仅更新仍为旧值 25 的行，避免覆盖用户已显式配置的值。
            migrationBuilder.Sql("UPDATE Agents SET MaxIterations = 0 WHERE MaxIterations = 25;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回滚无意义（无法恢复各行当初的真实值），保持空实现。
        }
    }
}
