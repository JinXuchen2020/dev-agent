using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Infrastructure.Sandbox;
using AgentPlatform.Infrastructure.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using Xunit.Sdk;

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

    [SkippableFact]
    public async Task Run_Command_Executes_In_WorkspaceRoot()
    {
        // 验证目标：run_command 在工作区根目录执行（而非进程默认 CWD）。
        // 沙箱在 Windows 优先 Git Bash（agent 命令为 bash 语法），故用 bash 原生 `pwd`
        // 并在无可用 bash 的机器上跳过（Linux/CI ubuntu 天然满足）。
        Skip.IfNot(DetectWorkingBash(), "本机无可用的真实 bash（缺 Git Bash 且 System32 bash 为 WSL 桩），跳过该用例。");

        using var executor = CreateExecutor();
        var result = await executor.ExecuteAsync(
            Tool("run_command", "run"),
            "{\"command\":\"pwd\"}");
        Assert.True(result.Success, result.Output + result.ErrorMessage);
        Assert.Contains("ap_workspace_", result.Output);
    }

    /// <summary>
    /// 与生产 ProcessCodeSandbox.ResolveBashPath 同判据的可用性探测：
    /// where bash.exe 的候选须排除 System32/SysWOW64/WindowsApps（WSL 桩），
    /// 且通过 <c>bash -c echo</c> 实测。Git Bash 标准安装路径优先。
    /// </summary>
    private static bool DetectWorkingBash()
    {
        var candidates = new[]
        {
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files\Git\usr\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\usr\bin\bash.exe",
        };
        foreach (var path in candidates)
            if (System.IO.File.Exists(path) && ProbeBashEcho(path))
                return true;

        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("where.exe", "bash.exe")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc is null) return false;
            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            foreach (var raw in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var path = raw.Trim('"');
                if (path.Length == 0 || !System.IO.File.Exists(path)) continue;
                if (path.Contains("\\System32\\", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("\\SysWOW64\\", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ProbeBashEcho(path)) return true;
            }
        }
        catch
        {
            // where.exe 不可用
        }
        return false;
    }

    private static bool ProbeBashEcho(string bashPath)
    {
        try
        {
            using var probe = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(bashPath, "-c \"echo ok\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (probe is null) return false;
            var output = probe.StandardOutput.ReadToEnd();
            probe.WaitForExit(3000);
            return probe.ExitCode == 0 && output.Trim() == "ok";
        }
        catch
        {
            return false;
        }
    }
}