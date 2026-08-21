namespace AgentPlatform.Domain.Enums;

/// <summary>
/// Represents the origin of a tool definition available to agents.
/// </summary>
public enum ToolSource
{
    /// <summary>The tool is a built-in native tool provided by the platform.</summary>
    NativeTool,

    /// <summary>The tool is provided by a skill package.</summary>
    SkillPackage,

    /// <summary>The tool is provided by an external MCP (Model Context Protocol) server.</summary>
    McpServer,

    /// <summary>The tool lets the agent read/write/run inside its isolated workspace sandbox (Codex-style coding autonomy).</summary>
    Workspace
}
