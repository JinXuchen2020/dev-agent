using System.Diagnostics;
using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Infrastructure.Sandbox;

/// <summary>
/// 进程沙箱的 OS 级隔离抽象。按平台与 <c>SandboxSettings.OsIsolation</c> 解析为具体实现：
/// Windows + JobObject → <see cref="JobObjectSandboxIsolation"/>（资源限额）；
/// Windows + AppContainer/Full → <see cref="AppContainerSandboxIsolation"/>（真实禁网 + 资源限额）；
/// 非 Windows / Off / 不支持 → <see cref="NullSandboxIsolation"/>（仅环境标记缓解项）。
/// 所有实现均为 fail-safe：任何 OS 机制不可用都不阻断代码执行。
/// </summary>
internal interface ISandboxIsolation
{
    /// <summary>本隔离器是否自行启动进程（AppContainer 为 true；JobObject / Null 为 false，仅事后挂接）。</summary>
    bool CanLaunch { get; }

    /// <summary>
    /// 自行在隔离环境中启动进程并返回结果。不适用或失败（含 AppContainer 启动失败）时返回 <c>null</c>，
    /// 调用方据此回退到常规 <see cref="System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo)"/> 路径。
    /// </summary>
    Task<SandboxResult?> TryLaunchAsync(
        string fileName, string arguments, int timeoutSeconds, CancellationToken ct, string source, string language);

    /// <summary>进程已启动后挂接隔离（JobObject 赋权；Null 无操作）。返回是否成功挂接。</summary>
    bool Attach(Process process);

    /// <summary>本隔离器施加的隔离强度（Strong=Docker / Weak=F11 进程级 / None=无 OS 级隔离），供 <see cref="SandboxResult.IsolationStrength"/> 回传。</summary>
    IsolationStrength Strength { get; }
}
