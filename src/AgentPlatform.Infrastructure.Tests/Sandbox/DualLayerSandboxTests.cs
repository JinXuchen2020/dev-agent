#nullable disable
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Sandbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Sandbox;

/// <summary>
/// F34 双层隔离测试：Docker 默认强隔离 + F11 进程级兜底。
/// Docker 真实执行路径依赖守护进程，本沙箱无 daemon 故跳过；重点验证 fail-safe 回退、模式切换与隔离强度标注。
/// </summary>
public class DualLayerSandboxTests
{
    private static ILogger<T> L<T>() => Substitute.For<ILogger<T>>();

    private sealed class StubProbe : IDockerProbe
    {
        public StubProbe(bool available) => IsAvailable = available;
        public bool IsAvailable { get; }
    }

    // ── Docker 守护进程探测（fail-safe）──

    [Fact]
    public void DockerProbe_NoDaemon_IsUnavailable()
    {
        // 本沙箱无 Docker 守护进程：探测应失败且不抛异常（fail-safe 回退进程级隔离）。
        var probe = new DockerProbe(L<DockerProbe>());
        Assert.False(probe.IsAvailable);
    }

    // ── DockerSandboxIsolation 模式切换 ──

    [Fact]
    public void DockerSandboxIsolation_ProbeUnavailable_CanLaunchFalse()
    {
        var settings = new SandboxSettings { Provider = "Docker", AllowedLanguages = new[] { "python" } };
        var docker = new DockerCodeSandbox(L<DockerCodeSandbox>(), Options.Create(settings));
        var isolation = new DockerSandboxIsolation(L<DockerSandboxIsolation>(), new StubProbe(false), docker);

        Assert.False(isolation.CanLaunch);
        Assert.Equal(IsolationStrength.Strong, isolation.Strength); // 强度声明仍为 Strong（仅当可用时启用）
    }

    [Fact]
    public void DockerSandboxIsolation_ProbeAvailable_CanLaunchTrue()
    {
        // 仅验证属性切换（不实际连 Docker 守护进程）。
        var settings = new SandboxSettings { Provider = "Docker", AllowedLanguages = new[] { "python" } };
        var docker = new DockerCodeSandbox(L<DockerCodeSandbox>(), Options.Create(settings));
        var isolation = new DockerSandboxIsolation(L<DockerSandboxIsolation>(), new StubProbe(true), docker);

        Assert.True(isolation.CanLaunch);
        Assert.Equal(IsolationStrength.Strong, isolation.Strength);
    }

    [SkippableFact]
    public void DockerSandboxIsolation_Attach_ReturnsFalse()
    {
        // echo 仅为非 Windows 可执行；Windows 下 Process.Start("echo") 会失败，故跳过（Attach 在任何平台恒返回 false）。
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "echo 仅为非 Windows 可执行，Windows 跳过（Attach 在所有平台恒返回 false）");

        // 容器自带网络/资源/rootfs 隔离，无需再挂 Job Object。
        var settings = new SandboxSettings { Provider = "Docker" };
        var docker = new DockerCodeSandbox(L<DockerCodeSandbox>(), Options.Create(settings));
        var isolation = new DockerSandboxIsolation(L<DockerSandboxIsolation>(), new StubProbe(true), docker);
        using var process = Process.Start(new ProcessStartInfo("echo", "x")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        });
        Assert.NotNull(process);
        Assert.False(isolation.Attach(process));
    }

    [SkippableFact]
    public async Task DockerSandboxIsolation_DockerAvailable_ReturnsStrongResult()
    {
        // 仅当 Docker 守护进程真实可达时运行（本沙箱无 daemon，跳过；CI ubuntu-latest 实测）。
        var probe = new DockerProbe(L<DockerProbe>());
        Skip.IfNot(probe.IsAvailable, "Docker 守护进程不可用，跳过强隔离实测（CI ubuntu-latest 实测）");

        var settings = new SandboxSettings
        {
            Provider = "Docker",
            TimeoutSeconds = 30,
            AllowedLanguages = new[] { "python" },
            MaxOutputBytes = 65536,
        };
        var docker = new DockerCodeSandbox(L<DockerCodeSandbox>(), Options.Create(settings));
        var isolation = new DockerSandboxIsolation(L<DockerSandboxIsolation>(), probe, docker);

        var r = await isolation.TryLaunchAsync("python", string.Empty, 30, default, "print('docker_ok')", "python");
        Assert.NotNull(r);
        Assert.True(r.Success, $"Docker 强隔离下代码应执行：Stderr='{r.Stderr}'");
        Assert.Equal(IsolationStrength.Strong, r.IsolationStrength);
        Assert.Contains("docker_ok", r.Stdout);
    }

    // ── 回退路径：隔离强度标注正确 ──

    [SkippableFact]
    public async Task ProcessCodeSandbox_FallbackToJobObject_ReportsWeakStrength()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "JobObject 回退为 Windows 专属，非 Windows 走 Null（None）");

        // 模拟 DI 工厂在 Docker 不可用时的回退选择：注入 JobObjectSandboxIsolation。
        var settings = new SandboxSettings
        {
            Provider = "Docker",
            TimeoutSeconds = 30,
            AllowedLanguages = new[] { "python" },
            OsIsolation = OsIsolationMode.JobObject,
            MaxProcessCount = 16,
            MemoryLimitBytes = 256L * 1024 * 1024,
            CpuRatePercent = 50,
            MaxOutputBytes = 65536,
        };
        var isolation = new JobObjectSandboxIsolation(L<JobObjectSandboxIsolation>(), Options.Create(settings));
        var sb = new ProcessCodeSandbox(L<ProcessCodeSandbox>(), Options.Create(settings), isolation);

        var r = await sb.RunCodeAsync("print('fallback_weak_ok')", "python", 30, default);

        Assert.True(r.Success, $"Docker 不可用须回退进程级隔离并成功执行：Stdout='{r.Stdout}' Stderr='{r.Stderr}'");
        Assert.Contains("fallback_weak_ok", r.Stdout);
        Assert.Equal(IsolationStrength.Weak, r.IsolationStrength);
    }

    // ── SandboxResult 向后兼容 ──

    [Fact]
    public void SandboxResult_DefaultIsolationStrength_BackwardCompatible_Weak()
    {
        // 既有 5 参构造调用应继续编译且默认 Weak（不破坏任何现有调用方）。
        var r = new SandboxResult(true, "out", "err", 0, 0);
        Assert.Equal(IsolationStrength.Weak, r.IsolationStrength);
    }
}
