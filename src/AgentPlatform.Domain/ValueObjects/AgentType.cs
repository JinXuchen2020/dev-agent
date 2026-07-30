using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;

namespace AgentPlatform.Domain.ValueObjects;

/// <summary>
/// Represents an agent type value object that defines the role identity of an agent
/// within the multi-agent collaboration pipeline. Includes both built-in predefined roles
/// and support for custom user-defined roles.
/// </summary>
public sealed record AgentType
{
    /// <summary>
    /// Gets the unique code identifying this agent role (e.g., "development", "architecture").
    /// This code is used for storage, lookup, and matching in the database and API.
    /// </summary>
    public string RoleCode { get; init; } = null!; // EF Core proxy

    /// <summary>
    /// Gets the human-readable display name of this agent role (e.g., "Senior Developer").
    /// Used for UI presentation and logging.
    /// </summary>
    public string DisplayName { get; init; } = null!; // EF Core proxy

    /// <summary>
    /// Gets a description of the responsibilities and capabilities of this agent role.
    /// </summary>
    public string Description { get; init; } = null!; // EF Core proxy

    /// <summary>
    /// Private parameterless constructor for EF Core materialization.
    /// </summary>
    private AgentType() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentType"/> record with the specified role identity values.
    /// </summary>
    /// <param name="roleCode">The unique code identifying the agent role (e.g., "development"). Must not be null or whitespace.</param>
    /// <param name="displayName">The human-readable display name of the agent role (e.g., "Senior Developer"). Must not be null or whitespace.</param>
    /// <param name="description">A description of the agent role's responsibilities. Must not be null or whitespace.</param>
    public AgentType(string roleCode, string displayName, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        RoleCode = roleCode;
        DisplayName = displayName;
        Description = description;
    }

    /// <summary>
    /// Predefined Requirements Analyst agent type with role code "requirement".
    /// </summary>
    public static readonly AgentType Requirement = new(BuiltInRoleCatalog.Requirement, "需求分析", "负责需求收集、分析与整理");

    /// <summary>
    /// Predefined Product Manager agent type with role code "product".
    /// </summary>
    public static readonly AgentType Product = new(BuiltInRoleCatalog.Product, "产品经理", "负责产品规划、功能设计与路线图");

    /// <summary>
    /// Predefined Architect agent type with role code "architecture".
    /// </summary>
    public static readonly AgentType Architecture = new(BuiltInRoleCatalog.Architecture, "系统架构", "负责系统架构设计和技术选型");

    /// <summary>
    /// Predefined Developer agent type with role code "development".
    /// </summary>
    public static readonly AgentType Development = new(BuiltInRoleCatalog.Development, "代码实现", "负责功能开发和代码实现");

    /// <summary>
    /// Predefined Tester agent type with role code "testing".
    /// </summary>
    public static readonly AgentType Testing = new(BuiltInRoleCatalog.Testing, "质量保证", "负责功能测试和质量保证");

    /// <summary>
    /// Predefined Tech Writer agent type with role code "documentation".
    /// </summary>
    public static readonly AgentType Documentation = new(BuiltInRoleCatalog.Documentation, "文档编写", "负责技术文档和用户文档编写");

    /// <summary>
    /// Predefined Reviewer agent type with role code "reviewer".
    /// </summary>
    public static readonly AgentType Reviewer = new(BuiltInRoleCatalog.Reviewer, "评审专家", "负责代码与设计评审");

    /// <summary>
    /// Gets the collection of all predefined agent types. The role codes are kept in
    /// lock-step with <see cref="Aggregates.AgentRoleDefinitions.BuiltInRoleCatalog"/> (DB-authoritative set)
    /// and verified by an architecture parity test.
    /// </summary>
    public static IReadOnlyList<AgentType> Predefined => new[]
    {
        Requirement, Product, Architecture, Development, Testing, Documentation, Reviewer
    };

    /// <summary>
    /// Attempts to find a predefined agent type by its role code.
    /// </summary>
    /// <param name="roleCode">The role code to search for (e.g., "development").</param>
    /// <returns>The matching <see cref="AgentType"/> if found; otherwise <c>null</c>.</returns>
    /// <remarks>
    /// Returns <c>null</c> if the role code does not match any predefined agent type.
    /// Use <see cref="FromCodeOrThrow(string)"/> to throw an exception for invalid codes.
    /// </remarks>
    public static AgentType? FromCode(string roleCode) => Predefined.FirstOrDefault(r => r.RoleCode == roleCode);

    /// <summary>
    /// Attempts to find a predefined agent type by its role code and throws if not found.
    /// </summary>
    /// <param name="roleCode">The role code to search for (e.g., "development").</param>
    /// <returns>The matching <see cref="AgentType"/> if found.</returns>
    /// <exception cref="ArgumentException">Thrown when the role code does not match any predefined agent type.</exception>
    public static AgentType FromCodeOrThrow(string roleCode)
    {
        var result = FromCode(roleCode);
        if (result is null)
        {
            throw new ArgumentException($"Agent type with role code '{roleCode}' not found. " +
                $"Valid codes are: {string.Join(", ", Predefined.Select(r => r.RoleCode))}",
                nameof(roleCode));
        }
        return result;
    }
}
