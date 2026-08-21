using System.IO;
using System.Text;
using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Tools;

/// <summary>
/// Executes the platform workspace tools (<c>read_file</c>, <c>write_file</c>, <c>edit_file</c>,
/// <c>list_files</c>, <c>run_command</c>, <c>git_diff</c>) strictly inside a per-run isolated directory,
/// reusing the existing code-sandbox substrate (network-disabled, resource-limited, output-truncated).
/// Path-escape and dangerous-command guardrails keep an autonomous agent from touching the host.
/// </summary>
internal sealed class WorkspaceToolExecutor : IToolExecutor, IDisposable
{
    private readonly ILogger<WorkspaceToolExecutor> _logger;
    private readonly ICodeSandbox _sandbox;
    private readonly object _rootLock = new();
    private string? _root;

    // Commands that could damage the host are rejected outright before reaching the sandbox.
    private static readonly string[] ForbiddenCommandTokens =
    {
        "rm -rf /", "mkfs", "dd if=", ":(){", "shutdown", "reboot", "format c:", "> /dev/sd"
    };

    private const int MaxFileBytes = 200_000;

    // UTF-8 without BOM: Encoding.UTF8 writes a BOM preamble, which corrupts round-tripped text.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public WorkspaceToolExecutor(ILogger<WorkspaceToolExecutor> logger, ICodeSandbox sandbox)
    {
        _logger = logger;
        _sandbox = sandbox;
    }

    public ToolSource Source => ToolSource.Workspace;

    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolDefinition tool, string parametersJson, CancellationToken ct = default)
    {
        try
        {
            switch (tool.Name)
            {
                case "read_file":
                {
                    var path = GetString(parametersJson, "path") ?? string.Empty;
                    var full = ResolvePath(path);
                    if (!File.Exists(full))
                        return ToolExecutionResult.Fail($"File not found: {path}");
                    var bytes = await File.ReadAllBytesAsync(full, ct);
                    var truncated = bytes.Length > MaxFileBytes ? bytes.AsSpan(0, MaxFileBytes).ToArray() : bytes;
                    return ToolExecutionResult.Ok(Encoding.UTF8.GetString(truncated));
                }
                case "write_file":
                {
                    var path = GetString(parametersJson, "path") ?? string.Empty;
                    var text = GetString(parametersJson, "text") ?? string.Empty;
                    var full = ResolvePath(path);
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    await File.WriteAllTextAsync(full, text, Utf8NoBom, ct);
                    return ToolExecutionResult.Ok($"Wrote {text.Length} chars to {path}");
                }
                case "edit_file":
                {
                    var path = GetString(parametersJson, "path") ?? string.Empty;
                    var old = GetString(parametersJson, "old") ?? string.Empty;
                    var neu = GetString(parametersJson, "new") ?? string.Empty;
                    var full = ResolvePath(path);
                    if (!File.Exists(full))
                        return ToolExecutionResult.Fail($"File not found: {path}");
                    var content = await File.ReadAllTextAsync(full, Encoding.UTF8, ct);
                    if (!content.Contains(old))
                        return ToolExecutionResult.Fail($"Pattern not found in {path}");
                    var updated = content.Replace(old, neu, StringComparison.Ordinal);
                    await File.WriteAllTextAsync(full, updated, Utf8NoBom, ct);
                    return ToolExecutionResult.Ok($"Edited {path}");
                }
                case "list_files":
                {
                    var pattern = GetString(parametersJson, "pattern");
                    var root = EnsureRoot();
                    var files = string.IsNullOrWhiteSpace(pattern)
                        ? Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                        : Directory.GetFiles(root, pattern, SearchOption.AllDirectories);
                    var rel = files.Select(f => "/" + Path.GetRelativePath(root, f).Replace('\\', '/'));
                    return ToolExecutionResult.Ok(string.Join("\n", rel));
                }
                case "run_command":
                {
                    var command = GetString(parametersJson, "command") ?? string.Empty;
                    if (IsForbidden(command))
                        return ToolExecutionResult.Fail("Command rejected by workspace safety guardrail.");
                    var result = await _sandbox.RunCommandAsync(command, workingDirectory: EnsureRoot(), ct: ct);
                    var output = result.Stdout ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(result.Stderr))
                        output += (output.Length > 0 ? "\n" : string.Empty) + result.Stderr;
                    return new ToolExecutionResult(result.Success, output,
                        ErrorMessage: result.Success ? null : $"ExitCode={result.ExitCode}");
                }
                case "git_diff":
                {
                    var result = await _sandbox.RunCommandAsync("git diff", workingDirectory: EnsureRoot(), ct: ct);
                    return new ToolExecutionResult(result.Success, result.Stdout + result.Stderr,
                        ErrorMessage: result.Success ? null : $"ExitCode={result.ExitCode}");
                }
                default:
                    return ToolExecutionResult.Fail($"Unknown workspace tool: {tool.Name}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Workspace tool {Tool} failed", tool.Name);
            return ToolExecutionResult.Fail(ex.Message);
        }
    }

    private static bool IsForbidden(string command)
    {
        var lower = command.ToLowerInvariant();
        return ForbiddenCommandTokens.Any(t => lower.Contains(t));
    }

    private string EnsureRoot()
    {
        lock (_rootLock)
        {
            if (_root is null)
            {
                _root = Path.Combine(Path.GetTempPath(), $"ap_workspace_{Guid.NewGuid():N}");
                Directory.CreateDirectory(_root);
            }
            return _root;
        }
    }

    private string ResolvePath(string relativePath)
    {
        var root = EnsureRoot();
        var full = Path.GetFullPath(Path.Combine(root, relativePath ?? string.Empty));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) &&
            !full.Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Path '{relativePath}' escapes the workspace root and is not allowed.");
        }
        return full;
    }

    private static string? GetString(string json, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "{}");
            if (doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        }
        catch (JsonException)
        {
            // fall through to null
        }
        return null;
    }

    public void Dispose()
    {
        if (_root is null) return;
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
