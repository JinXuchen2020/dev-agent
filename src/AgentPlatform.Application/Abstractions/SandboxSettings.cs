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

    /// <summary>是否允许沙箱访问网络（默认 false，禁网）。进程级沙箱无法在 OS 层强制隔离，此标志仅作记录与未来 Docker 模式使用。</summary>
    public bool NetworkEnabled { get; set; } = false;

    /// <summary>输出截断字节上限，防止超大输出撑爆上下文。</summary>
    public int MaxOutputBytes { get; set; } = 65536;

    /// <summary>
    /// 每语言的解释器命令覆盖（key = 语言小写，value = 可执行文件路径或命令）。
    /// 为空时按语言使用默认命令名（python → <c>python</c>，javascript → <c>node</c>）。
    /// 测试可在本字典中指向确定可用的解释器路径。
    /// </summary>
    public Dictionary<string, string> InterpreterPaths { get; set; } = new();
}
