namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 进程沙箱的 OS 级隔离强度。
/// 控制进程沙箱（ProcessCodeSandbox）在启动用户代码时是否施加 OS 层资源限额与网络隔离。
/// 任何 OS 机制不可用时均透明回退到环境标记缓解项，绝不阻断执行。
/// </summary>
public enum OsIsolationMode
{
    /// <summary>不施加任何 OS 级隔离（仅语言白名单 + 超时杀 + 输出截断 + 环境标记缓解项）。</summary>
    Off = 0,

    /// <summary>
    /// Windows Job Object 资源限额（作业/进程内存上限、活动进程数上限防 fork 炸、CPU 速率硬上限）。
    /// 不需管理员权限，默认安全开启。
    /// </summary>
    JobObject = 1,

    /// <summary>
    /// 在 Windows AppContainer（无 internetClient 能力）内启动解释器，真实阻断出网；仍叠加 Job Object 资源限额。
    /// 需主机一次性准备解释器目录的 <c>ALL APPLICATION PACKAGES</c> 读数 ACL，否则启动失败并 fail-safe 回退。
    /// </summary>
    AppContainer = 2,

    /// <summary>Job Object 资源限额 + AppContainer 网络隔离同时启用（等价于同时包含两者）。</summary>
    Full = 3,
}
