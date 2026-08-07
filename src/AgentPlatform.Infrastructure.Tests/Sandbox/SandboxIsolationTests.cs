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
/// F11 进程沙箱 OS 级隔离测试：Job Object 资源限额（Windows 真实施加、可测）+ AppContainer 网络隔离（fail-safe 不阻断执行）。
/// AppContainer 依赖主机解释器目录的 ALL APPLICATION PACKAGES 读 ACL，本沙箱不可达时透明回退，故以「不抛异常 / 不伪造失败」为不变量断言。
/// </summary>
public class SandboxIsolationTests
{
    private static ILogger<T> L<T>() => Substitute.For<ILogger<T>>();

    // ── Job Object 资源限额（Windows 真实路径，可测）──

    [SkippableFact]
    public async Task JobObjectSandboxIsolation_Windows_Attach_ExecutesCodeSuccessfully()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "Job Object 为 Windows 专属，非 Windows 环境跳过（将走 Null 隔离器）");

        var settings = new SandboxSettings
        {
            Provider = "Process",
            TimeoutSeconds = 30,
            AllowedLanguages = new[] { "python", "javascript" },
            OsIsolation = OsIsolationMode.JobObject,
            MaxProcessCount = 16,
            MemoryLimitBytes = 256L * 1024 * 1024,
            CpuRatePercent = 50,
            MaxOutputBytes = 65536,
        };
        var isolation = new JobObjectSandboxIsolation(L<JobObjectSandboxIsolation>(), Options.Create(settings));
        var sb = new ProcessCodeSandbox(L<ProcessCodeSandbox>(), Options.Create(settings), isolation);

        var r = await sb.RunCodeAsync("print('job_ok')", "python", 30, default);

        Assert.True(r.Success, $"JobObject 隔离下代码应正常执行：Stdout='{r.Stdout}' Stderr='{r.Stderr}' ExitCode={r.ExitCode}");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("job_ok", r.Stdout);
    }

    [SkippableFact]
    public void JobObjectSandboxIsolation_NonWindows_AttachReturnsFalse()
    {
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "仅在非 Windows 平台验证 Attach 返回 false（NULL 隔离器不施加 Job Object）");

        var settings = new SandboxSettings { OsIsolation = OsIsolationMode.JobObject };
        var isolation = new JobObjectSandboxIsolation(L<JobObjectSandboxIsolation>(), Options.Create(settings));
        using var process = Process.Start(new ProcessStartInfo("echo", "x")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        });
        Assert.NotNull(process);
        Assert.False(isolation.Attach(process));
    }

    [SkippableFact]
    public void WindowsJobObject_Direct_Assign_DoesNotThrow()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "WindowsJobObject 为 Windows 专属 P/Invoke，非 Windows 跳过");

        using var job = new WindowsJobObject(256L * 1024 * 1024, 50, 16, L<WindowsJobObject>());
        using var process = Process.Start(new ProcessStartInfo("python", "-c \"print('job_direct_ok')\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Assert.NotNull(process);
        // 不应抛异常；Job Object 资源限额挂接后进程仍可正常退出。
        job.Assign(process);
        Assert.True(process.WaitForExit(15000));
        Assert.Equal(0, process.ExitCode);
    }

    // ── AppContainer 网络隔离（fail-safe 不变量）──

    [SkippableFact]
    public async Task AppContainerSandboxIsolation_TryLaunch_NeverReturnsFailedResult()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "AppContainer 为 Windows 专属，非 Windows 跳过");

        var settings = new SandboxSettings
        {
            OsIsolation = OsIsolationMode.AppContainer,
            MaxProcessCount = 16,
            MemoryLimitBytes = 256L * 1024 * 1024,
            CpuRatePercent = 50,
            MaxOutputBytes = 65536,
        };
        var isolation = new AppContainerSandboxIsolation(L<AppContainerSandboxIsolation>(), Options.Create(settings));

        // 不变量：环境不可达时返回 null（由调用方回退）；可达时返回成功结果。
        // 绝不返回「失败但非 null」的结果（那会伪造一次失败执行）。
        var r = await isolation.TryLaunchAsync("python", string.Empty, 30, default, "print('x')", "python");
        Assert.True(r is null || r.Success,
            $"AppContainer 失败安全不变量被破坏：返回了失败结果 Stdout='{r?.Stdout}' Stderr='{r?.Stderr}'");
    }

    [SkippableFact]
    public async Task ProcessCodeSandbox_WithAppContainerIsolation_StillRunsCode_ViaFailSafe()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "AppContainer 为 Windows 专属，非 Windows 跳过");

        var settings = new SandboxSettings
        {
            Provider = "Process",
            TimeoutSeconds = 30,
            AllowedLanguages = new[] { "python" },
            OsIsolation = OsIsolationMode.AppContainer,
            MaxProcessCount = 16,
            MemoryLimitBytes = 256L * 1024 * 1024,
            CpuRatePercent = 50,
            MaxOutputBytes = 65536,
        };
        // 即便 AppContainer 无法在本沙箱真正拉起（缺解释器 ACL），也必须透明回退到常规启动并成功执行。
        var isolation = new AppContainerSandboxIsolation(L<AppContainerSandboxIsolation>(), Options.Create(settings));
        var sb = new ProcessCodeSandbox(L<ProcessCodeSandbox>(), Options.Create(settings), isolation);

        var r = await sb.RunCodeAsync("print('appc_fallback_ok')", "python", 30, default);

        Assert.True(r.Success,
            $"AppContainer 隔离器失败安全回退应保证代码执行：Stdout='{r.Stdout}' Stderr='{r.Stderr}' ExitCode={r.ExitCode}");
        Assert.Contains("appc_fallback_ok", r.Stdout);
    }
}
