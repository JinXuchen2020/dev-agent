using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Sandbox;

/// <summary>
/// Windows Job Object 隔离：进程启动后将其（及后代）纳入 Job Object，施加资源限额。
/// <see cref="CanLaunch"/> 为 false（不自行启动进程，仅事后 <see cref="Attach"/>）。
/// 资源限额句柄随进程退出自动释放（借 process.Exited 事件 + 字典追踪）。
/// </summary>
internal sealed class JobObjectSandboxIsolation : ISandboxIsolation, IDisposable
{
    public bool CanLaunch => false;

    private readonly ILogger<JobObjectSandboxIsolation> _logger;
    private readonly SandboxSettings _settings;
    private readonly ConcurrentDictionary<int, WindowsJobObject> _active = new();

    public JobObjectSandboxIsolation(ILogger<JobObjectSandboxIsolation> logger, IOptions<SandboxSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public Task<SandboxResult?> TryLaunchAsync(
        string fileName, string arguments, int timeoutSeconds, CancellationToken ct, string source, string language)
        => Task.FromResult<SandboxResult?>(null);

    public bool Attach(Process process)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        var job = new WindowsJobObject(_settings.MemoryLimitBytes, _settings.CpuRatePercent, _settings.MaxProcessCount, _logger);
        job.Assign(process);
        _active[process.Id] = job;
        // 进程退出后释放 Job Object 句柄（关闭句柄不会终止进程，未设 KILL_ON_JOB_CLOSE）。
        process.EnableRaisingEvents = true;
        process.Exited += OnExited;
        return true;
    }

    private void OnExited(object? sender, EventArgs e)
    {
        if (sender is Process p && _active.TryRemove(p.Id, out var job))
        {
            // 仅释放 Job Object 句柄；Process 生命周期由其拥有者（ProcessCodeSandbox）管理，此处不可 Dispose，
            // 否则 CaptureAsync 读取 ExitCode 时会抛 "No process is associated with this object"。
            job.Dispose();
            p.Exited -= OnExited;
        }
    }

    /// <summary>
    /// 作用域结束时释放所有仍活跃的 Job Object 句柄。防御性兜底：若进程退出时 <see cref="Process.Exited"/> 事件
    /// 因竞态早于 <see cref="Process"/> 释放而未能触发 <see cref="OnExited"/>，此处保证句柄不泄漏。
    /// 已退出的进程句柄在字典中已移除，故仅清理残留项。
    /// </summary>
    public void Dispose()
    {
        foreach (var job in _active.Values)
        {
            try { job.Dispose(); }
            catch { /* 句柄可能已失效 */ }
        }
        _active.Clear();
    }
}
