namespace AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;

/// <summary>
/// Single source of truth for the platform's built-in agent role codes.
/// The database seed (<c>DatabaseInitializer</c>) and the <see cref="ValueObjects.AgentType"/>
/// value-object mirror are both derived from this catalog so the two role directories
/// can never drift apart again (guarded by an architecture parity test).
/// </summary>
public static class BuiltInRoleCatalog
{
    /// <summary>Built-in role code: 需求分析 (Requirements analysis).</summary>
    public const string Requirement = "requirement";

    /// <summary>Built-in role code: 产品经理 (Product management).</summary>
    public const string Product = "product";

    /// <summary>Built-in role code: 系统架构 (System architecture).</summary>
    public const string Architecture = "architecture";

    /// <summary>Built-in role code: 代码实现 (Development).</summary>
    public const string Development = "development";

    /// <summary>Built-in role code: 质量保证 (Quality assurance).</summary>
    public const string Testing = "testing";

    /// <summary>Built-in role code: 文档编写 (Documentation).</summary>
    public const string Documentation = "documentation";

    /// <summary>Built-in role code: 评审专家 (Review).</summary>
    public const string Reviewer = "reviewer";

    /// <summary>
    /// The canonical, DB-authoritative set of built-in role codes.
    /// </summary>
    public static IReadOnlyList<string> Codes { get; } = new[]
    {
        Requirement,
        Product,
        Architecture,
        Development,
        Testing,
        Documentation,
        Reviewer,
    };
}
