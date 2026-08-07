using System.Diagnostics;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Sandbox;

/// <summary>
/// 无 OS 级隔离（非 Windows 平台 / <c>OsIsolation=Off</c> / 不支持的运行时）。
/// 仅保留调用方的环境标记缓解项，启动记一次告警；不自行启动进程、不挂接。
/// </summary>
internal sealed class NullSandboxIsolation : ISandboxIsolation
{
    private readonly ILogger<NullSandboxIsolation> _logger;

    public NullSandboxIsolation(ILogger<NullSandboxIsolation> logger)
    {
        _logger = logger;
        _logger.LogWarning(
            "OS 级沙箱隔离未启用（平台非 Windows 或 OsIsolation=Off）：代码执行仅依赖环境标记 + 语言白名单 + 超时杀等缓解项，" +
            "无 OS 层资源限额/网络隔离。Windows 平台可设 Sandbox:OsIsolation=JobObject/AppContainer/Full 启用。");
    }

    public bool CanLaunch => false;

    public Task<SandboxResult?> TryLaunchAsync(
        string fileName, string arguments, int timeoutSeconds, CancellationToken ct, string source, string language)
        => Task.FromResult<SandboxResult?>(null);

    public bool Attach(Process process) => false;
}
