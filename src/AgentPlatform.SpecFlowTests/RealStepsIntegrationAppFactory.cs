using System.Collections.Generic;
using System.Diagnostics;
using AgentPlatform.SpecFlowTests;

namespace AgentPlatform.SpecFlowTests;

/// <summary>
/// F12 专用集成工厂变体：在保留真实 IStepExecutor（Code/Tool 真实执行）的前提下运行后端，
/// 与基工厂默认「剥除真实执行器替换 ConfigurableStepExecutor（假输出）」相反。
///
/// 关键覆写（见 features/tool-code-e2e.md §3.1）：
/// - <see cref="DbPath"/> → 独立文件 test-integration-f12.db，避免与基工厂争用同一磁盘 SQLite；
/// - <see cref="StripStepExecutors"/> → false，保留真实 Code/Tool 执行链；
/// - <see cref="IntegrationConfiguration"/> → 追加 Sandbox:Provider=Process（跳过 Docker 探测/镜像拉取）
///   与 Sandbox:InterpreterPaths:python（指向当前环境可解析的 python 命令名，覆盖 CI ubuntu-latest
///   仅 python3 可用的情况）。
///
/// JWT 跨工厂可移植：复用基类 Security:JwtSecretKey 与 Tenant:DefaultTenantId，故基工厂签发的
/// admin JWT 对本工厂 API 同样有效（§3.4）。
/// </summary>
public sealed class RealStepsIntegrationAppFactory : IntegrationAppFactory
{
    /// <inheritdoc />
    protected override string DbPath => "test-integration-f12.db";

    /// <inheritdoc />
    protected override bool StripStepExecutors => false;

    /// <inheritdoc />
    protected override Dictionary<string, string?> IntegrationConfiguration
    {
        get
        {
            var cfg = base.IntegrationConfiguration;
            // 进程级沙箱：直接走 ProcessCodeSandbox 跑 python，跳过 Docker 守护进程探测与镜像拉取。
            cfg["Sandbox:Provider"] = "Process";
            // 指向当前环境可解析的 python 命令名（Windows=python；CI ubuntu-latest=python3），
            // 使 Code 节点真实执行路径稳定可跑，不依赖 PATH 上恰好叫 "python" 的二进制。
            cfg["Sandbox:InterpreterPaths:python"] = DetectPythonCommand();
            return cfg;
        }
    }

    /// <summary>
    /// 探测 PATH 上可解析的 python 命令名：优先 python，否则 python3；均不可用时兜底 python
    /// （让真实执行路径在环境缺失时以失败断言暴露，而非静默假绿）。
    /// </summary>
    private static string DetectPythonCommand()
    {
        foreach (var candidate in new[] { "python", "python3" })
        {
            try
            {
                var psi = new ProcessStartInfo(candidate, "--version")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                if (process is null)
                    continue;
                if (process.WaitForExit(2000) && process.ExitCode == 0)
                    return candidate;
            }
            catch
            {
                // 该候选不可用，尝试下一个。
            }
        }

        return "python";
    }
}
