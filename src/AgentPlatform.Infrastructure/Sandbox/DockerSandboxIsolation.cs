using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Sandbox;

/// <summary>
/// 容器级强隔离：复用 F9 <see cref="DockerCodeSandbox"/> 的容器执行能力，在 Docker 守护进程可用时
/// 经 <c>NetworkMode=none</c> + 内存限额 + 只读代码挂载（<c>:ro</c>）真实执行用户代码（<see cref="IsolationStrength.Strong"/>）。
/// 经由 <see cref="ISandboxIsolation"/> 接入 <see cref="ProcessCodeSandbox"/> 统一入口；
/// 不可用时 <see cref="CanLaunch"/>=<c>false</c>，调用方透明回退 F11 进程级路径。
/// </summary>
internal sealed class DockerSandboxIsolation : ISandboxIsolation
{
    private readonly ILogger<DockerSandboxIsolation> _logger;
    private readonly IDockerProbe _probe;
    private readonly DockerCodeSandbox _docker;

    public DockerSandboxIsolation(ILogger<DockerSandboxIsolation> logger, IDockerProbe probe, DockerCodeSandbox docker)
    {
        _logger = logger;
        _probe = probe;
        _docker = docker;
    }

    /// <summary>仅当 Docker 守护进程可用时为 true；否则 <see cref="ProcessCodeSandbox"/> 走回退路径。</summary>
    public bool CanLaunch => _probe.IsAvailable;

    /// <summary>容器隔离为强隔离。</summary>
    public IsolationStrength Strength => IsolationStrength.Strong;

    public async Task<SandboxResult?> TryLaunchAsync(
        string fileName, string arguments, int timeoutSeconds, CancellationToken ct, string source, string language)
    {
        if (!_probe.IsAvailable)
            return null;
        try
        {
            // 委托 F9 已真实化的容器执行：source=代码，language 决定镜像（python:3.12-slim / node:20-slim）。
            // 忽略 fileName/arguments（Docker 走代码注入，不经本地解释器路径）。
            // DockerCodeSandbox 构造 SandboxResult 时默认 Weak，此处以 `with` 升级为 Strong（强隔离标注，F34 核心契约）。
            var result = await _docker.RunCodeAsync(source, language, timeoutSeconds, ct).ConfigureAwait(false);
            return result with { IsolationStrength = IsolationStrength.Strong };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Docker 隔离执行失败，回退进程级隔离");
            return null;
        }
    }

    /// <summary>容器自带隔离（网络/资源/rootfs），无需再挂 Job Object。</summary>
    public bool Attach(Process process) => false;
}
