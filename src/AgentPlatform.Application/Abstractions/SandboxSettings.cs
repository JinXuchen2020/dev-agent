namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 配置代码沙箱与原生工具 HTTP 调用的安全边界。
/// 通过 <c>IOptions&lt;SandboxSettings&gt;</c> 注入（配置节 <c>Sandbox</c>）。
/// </summary>
public sealed class SandboxSettings
{
    /// <summary>沙箱提供方：<c>Docker</c>（需 Docker 运行环境）或 <c>Process</c>（进程级，默认且本沙箱可验证）。</summary>
    public string Provider { get; set; } = "Process";

    /// <summary>代码 / 命令执行的默认超时（秒）。</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>原生工具 HTTP 调用的默认超时（秒）。</summary>
    public int HttpTimeoutSeconds { get; set; } = 15;

    /// <summary>允许执行的语言白名单（小写）。非白名单语言拒绝执行。</summary>
    public string[] AllowedLanguages { get; set; } = { "python", "javascript", "csscript" };

    /// <summary>
    /// 是否允许沙箱访问网络（默认 false，禁网）。
    /// 当 <see cref="OsIsolation"/> 含 AppContainer 能力且本值为 false 时，经 AppContainer 真实阻断出网；
    /// 否则仅设 <c>AGENT_PLATFORM_SANDBOX_OFFLINE</c> 环境标记（best-effort 缓解项）。
    /// </summary>
    public bool NetworkEnabled { get; set; } = false;

    /// <summary>输出截断字节上限，防止超大输出撑爆上下文。</summary>
    public int MaxOutputBytes { get; set; } = 65536;

    /// <summary>
    /// OS 级隔离模式。默认 <see cref="OsIsolationMode.JobObject"/>（仅资源限额，不需管理员、无噪声）。
    /// <see cref="OsIsolationMode.AppContainer"/> / <see cref="OsIsolationMode.Full"/> 额外启用 AppContainer 网络隔离，
    /// 需主机已准备解释器目录的 <c>ALL APPLICATION PACKAGES</c> 读 ACL，否则 fail-safe 回退。
    /// </summary>
    public OsIsolationMode OsIsolation { get; set; } = OsIsolationMode.JobObject;

    /// <summary>作业内允许的最大活动进程数（防 fork 炸）。默认 16。</summary>
    public int MaxProcessCount { get; set; } = 16;

    /// <summary>作业内存上限（字节）。默认 256 MB。</summary>
    public long MemoryLimitBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>CPU 速率硬上限（百分比，1-100）。默认 50。</summary>
    public int CpuRatePercent { get; set; } = 50;

    /// <summary>
    /// 每语言的解释器命令覆盖（key = 语言小写，value = 可执行文件路径或命令）。
    /// 为空时按语言使用默认命令名（python → <c>python</c>，javascript → <c>node</c>）。
    /// 测试可在本字典中指向确定可用的解释器路径。
    /// </summary>
    public Dictionary<string, string> InterpreterPaths { get; set; } = new();
}
