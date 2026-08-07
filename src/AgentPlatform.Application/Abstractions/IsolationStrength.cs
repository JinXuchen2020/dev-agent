namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 沙箱隔离强度，用于结果可观测与显式告知用户隔离等级。
/// 由隔离层（ISandboxIsolation）决定并在 SandboxResult 中回传。
/// </summary>
public enum IsolationStrength
{
    /// <summary>无 OS 级隔离（仅环境标记缓解项，对应进程级 Null 隔离实现）。</summary>
    None,

    /// <summary>同内核弱隔离：Job Object 资源限额 / AppContainer 能力剥夺（对应 F11 进程级隔离）。</summary>
    Weak,

    /// <summary>容器/VM 强隔离：Docker NetworkMode=none + 资源限额 + read-only rootfs（对应 Docker 容器隔离实现）。</summary>
    Strong,
}
