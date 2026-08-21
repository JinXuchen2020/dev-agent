using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Infrastructure.Tools;

/// <summary>
/// Platform-provided workspace tools that let an autonomous agent read/write/run inside its isolated
/// sandbox — the hard prerequisite for Codex-style coding autonomy. Registered into the tool registry
/// at startup so they can be whitelisted per-agent via <see cref="Agent.AllowedToolNames"/>.
/// </summary>
public static class WorkspaceToolDefinitions
{
    // Fixed identifiers so seeding is idempotent across restarts.
    private static readonly Guid ReadFile = Guid.Parse("f1000000-0000-0000-0000-000000000001");
    private static readonly Guid WriteFile = Guid.Parse("f1000000-0000-0000-0000-000000000002");
    private static readonly Guid EditFile = Guid.Parse("f1000000-0000-0000-0000-000000000003");
    private static readonly Guid RunCommand = Guid.Parse("f1000000-0000-0000-0000-000000000004");
    private static readonly Guid ListFiles = Guid.Parse("f1000000-0000-0000-0000-000000000005");
    private static readonly Guid GitDiff = Guid.Parse("f1000000-0000-0000-0000-000000000006");

    private const string PlatformTenant = "00000000-0000-0000-0000-000000000000";

    /// <summary>Returns the full set of platform workspace tool definitions.</summary>
    public static IReadOnlyList<ToolDefinition> All() => new List<ToolDefinition>
    {
        new(ReadFile, "read_file", "Read a UTF-8 text file inside the agent workspace.",
            Obj(("path", "string", true)), "workspace.read_file", Guid.Parse(PlatformTenant), ToolSource.Workspace),
        new(WriteFile, "write_file", "Write UTF-8 text to a file inside the agent workspace, creating parent directories.",
            Obj(("path", "string", true), ("text", "string", true)), "workspace.write_file", Guid.Parse(PlatformTenant), ToolSource.Workspace),
        new(EditFile, "edit_file", "Replace the first occurrence of 'old' with 'new' in a workspace file (string diff).",
            Obj(("path", "string", true), ("old", "string", true), ("new", "string", true)), "workspace.edit_file", Guid.Parse(PlatformTenant), ToolSource.Workspace),
        new(RunCommand, "run_command", "Run a shell command inside the workspace sandbox (network-disabled, resource-limited).",
            Obj(("command", "string", true)), "workspace.run_command", Guid.Parse(PlatformTenant), ToolSource.Workspace),
        new(ListFiles, "list_files", "List files under the workspace root matching an optional glob pattern.",
            Obj(("pattern", "string", false)), "workspace.list_files", Guid.Parse(PlatformTenant), ToolSource.Workspace),
        new(GitDiff, "git_diff", "Show the git working-tree diff inside the workspace root.",
            Obj(), "workspace.git_diff", Guid.Parse(PlatformTenant), ToolSource.Workspace),
    };

    /// <summary>Registers the workspace tool definitions into the supplied registry (idempotent by name).</summary>
    public static void Seed(IToolRegistry registry)
    {
        foreach (var tool in All())
        {
            if (registry.GetByNameAsync(tool.Name).GetAwaiter().GetResult() is null)
                registry.Register(tool);
        }
    }

    private static string Obj(params (string Name, string Type, bool Required)[] props)
    {
        var required = props.Where(p => p.Required).Select(p => $"\"{p.Name}\"").ToList();
        var propJson = string.Join(",", props.Select(p => $"\"{p.Name}\":{{\"type\":\"{p.Type}\"}}"));
        return $"{{\"type\":\"object\",\"properties\":{{{propJson}}},\"required\":[{string.Join(",", required)}]}}";
    }
}
