using System;
using Docker.DotNet;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Sandbox;

/// <summary>
/// 一次性探测 Docker 守护进程可用性（构造时探测，结果缓存于 <see cref="IsAvailable"/>）。
/// 供 <see cref="ISandboxIsolation"/> 工厂决策是否启用 <see cref="DockerSandboxIsolation"/>。
/// 探测失败（无 daemon / 超时 / 异常）→ <c>false</c>，记告警，绝不抛异常、不阻断启动（fail-safe）。
/// </summary>
internal interface IDockerProbe
{
    /// <summary>Docker 守护进程当前是否可用。</summary>
    bool IsAvailable { get; }
}

internal sealed class DockerProbe : IDockerProbe
{
    private readonly ILogger<DockerProbe> _logger;

    public bool IsAvailable { get; }

    public DockerProbe(ILogger<DockerProbe> logger)
    {
        _logger = logger;
        IsAvailable = Probe();
    }

    private bool Probe()
    {
        try
        {
            using var client = new DockerClientConfiguration().CreateClient();
            // 同步探测并加 2s 硬超时兜底，避免无 daemon 环境阻塞 DI 解析。
            var ping = client.System.PingAsync();
            if (!ping.Wait(TimeSpan.FromSeconds(2)))
            {
                _logger.LogWarning("Docker 守护进程探测超时（>2s），回退进程级隔离");
                return false;
            }

            ping.GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Docker 守护进程不可用，回退进程级隔离");
            return false;
        }
    }
}
