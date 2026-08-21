using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Infrastructure.Sandbox;
using AgentPlatform.Infrastructure.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Tools;

/// <summary>
/// F29 验收 ④ / ⑤：Workspace 工具在真实进程沙箱内读/写/编辑/列出/跑命令，
/// 以及路径逃逸 / 危险命令黑名单护栏。
/// </summary>
public class WorkspaceToolExecutorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ToolDefinition Tool(string name, string description) => new(
        Guid.NewGuid(), name, description, "{}", name, TenantId, ToolSource.Workspace);

    private static WorkspaceToolExecutor CreateExecutor()
    {
        var settings = new SandboxSettings
        {
            Provider = "Process",
            TimeoutSeconds = 30,
            AllowedLanguages = new[] { "python", "javascript", "csscript" },
            NetworkEnabled = false,
            MaxOutputBytes = 65536,
        };
        var sandbox = new ProcessCodeSandbox(
            Substitute.For<ILogger<ProcessCodeSandbox>>(),
            Options.Create(settings),
            new NullSandboxIsolation(Substitute.For<ILogger<NullSandboxIsolation>>()));
        return new WorkspaceToolExecutor(
            Substitute.For<ILogger<WorkspaceToolExecutor>>(),
            sandbox);
    }

    [Fact]
    public async Task Write_Then_Read_RoundTrips_InsideWorkspace()
    {
        using var executor = CreateExecutor();
        var write = await executor.ExecuteAsync(Tool("write_file", "write"), "{\"path\":\"notes.txt\",\"text\":\"hello from sandbox\"}");
        Assert.True(write.Success);

        var read = await executor.ExecuteAsync(Tool("read_file", "read"), "{\"path\":\"notes.txt\"}");
        Assert.True(read.Success);
        Assert.Equal("hello from sandbox", read.Output);
    }

    [Fact]
    public async Task Edit_ReplacesPattern_Then_Read_Confirms()
    {
        using var executor = CreateExecutor();
        await executor.ExecuteAsync(Tool("write_file", "write"), "{\"path\":\"doc.txt\",\"text\":\"foo bar\"}");
        var edit = await executor.ExecuteAsync(Tool("edit_file", "edit"), "{\"path\":\"doc.txt\",\"old\":\"foo\",\"new\":\"baz\"}");
        Assert.True(edit.Success);

        var read = await executor.ExecuteAsync(Tool("read_file", "read"), "{\"path\":\"doc.txt\"}");
        Assert.Equal("baz bar", read.Output);
    }

    [Fact]
    public async Task List_Files_Returns_RootRelative_Paths()
    {
        using var executor = CreateExecutor();
        await executor.ExecuteAsync(Tool("write_file", "write"), "{\"path\":\"a.txt\",\"text\":\"a\"}");
        await executor.ExecuteAsync(Tool("write_file", "write"), "{\"path\":\"sub/b.txt\",\"text\":\"b\"}");

        var list = await executor.ExecuteAsync(Tool("list_files", "list"), "{}");
        Assert.True(list.Success);
        Assert.Contains("/a.txt", list.Output);
        Assert.Contains("/sub/b.txt", list.Output);
    }

    [Fact]
    public async Task Read_PathEscapingWorkspaceRoot_IsRejected()
    {
        using var executor = CreateExecutor();
        var result = await executor.ExecuteAsync(Tool("read_file", "read"), "{\"path\":\"../outside.txt\"}");
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("escapes", result.ErrorMessage);
    }

    [Fact]
    public async Task Run_ForbiddenCommand_IsRejectedByGuardrail()
    {
        using var executor = CreateExecutor();
        var result = await executor.ExecuteAsync(Tool("run_command", "run"), "{\"command\":\"rm -rf /\"}");
        Assert.False(result.Success);
        Assert.Contains("guardrail", result.ErrorMessage);
    }

    [Fact]
    public async Task Run_Command_Executes_In_WorkspaceRoot()
    {
        using var executor = CreateExecutor();
        // 先写一个打印工作目录的脚本，再以 run_command 执行 → 验证命令确实跑在工作区根目录。
        await executor.ExecuteAsync(
            Tool("write_file", "write"),
            "{\"path\":\"cwd.py\",\"text\":\"import os\\nprint(os.path.basename(os.getcwd()))\"}");
        var result = await executor.ExecuteAsync(
            Tool("run_command", "run"),
            "{\"command\":\"python cwd.py\"}");
        Assert.True(result.Success, result.Output + result.ErrorMessage);
        Assert.Contains("ap_workspace_", result.Output);
    }
}
