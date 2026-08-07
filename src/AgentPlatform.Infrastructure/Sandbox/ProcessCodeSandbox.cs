using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Sandbox;

/// <summary>
/// 进程级代码沙箱：通过 <see cref="System.Diagnostics.Process"/> 拉起 python / node 等解释器，
/// 真实运行代码并捕获 stdout / stderr / ExitCode / 耗时，超时即杀进程。
/// 不依赖 Docker，可在本沙箱运行与验证。通过 <see cref="ISandboxIsolation"/> 施加 OS 级隔离：
/// Job Object 资源限额 / AppContainer 真实禁网（按 <c>SandboxSettings.OsIsolation</c> 与平台解析，均 fail-safe）。
/// </summary>
internal sealed class ProcessCodeSandbox : ICodeSandbox
{
    private readonly ILogger<ProcessCodeSandbox> _logger;
    private readonly SandboxSettings _settings;
    private readonly ISandboxIsolation _isolation;

    public ProcessCodeSandbox(ILogger<ProcessCodeSandbox> logger, IOptions<SandboxSettings> settings,
        ISandboxIsolation isolation)
    {
        _logger = logger;
        _settings = settings.Value;
        _isolation = isolation;
    }

    public async Task<SandboxResult> RunCodeAsync(string code, string language,
        int timeoutSeconds = 30, CancellationToken ct = default)
    {
        var (cmd, ext) = ResolveInterpreter(language);
        if (cmd is null)
            return new SandboxResult(false, string.Empty, $"未找到 {language} 解释器或未授权", 1, 0);

        // OS 级隔离器自行启动（AppContainer 模式）：成功则直接返回隔离结果。
        if (_isolation.CanLaunch)
        {
            var isolated = await _isolation.TryLaunchAsync(cmd, EscapeArg(string.Empty), timeoutSeconds, ct, code, language)
                .ConfigureAwait(false);
            if (isolated is not null)
                return isolated;
            // 否则透明回退到常规 Process.Start 路径（资源限额仍由下方 Attach 兜底）。
        }

        var tempFile = WriteTempFileOrNull(code, ext);
        if (tempFile is null)
            return new SandboxResult(false, string.Empty, "临时文件写入失败", 1, 0);

        try
        {
            return await RunProcessAsync(cmd, EscapeArg(tempFile), timeoutSeconds, ct, code, language)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    public Task<SandboxResult> RunCommandAsync(string command,
        int timeoutSeconds = 30, CancellationToken ct = default)
    {
        var shell = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "/bin/sh";
        var shellArg = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"/c {EscapeArg(command)}"
            : $"-c {EscapeArg(command)}";
        return RunProcessAsync(shell, shellArg, timeoutSeconds, ct, command, "shell");
    }

    private async Task<SandboxResult> RunProcessAsync(string? fileName, string? arguments,
        int timeoutSeconds, CancellationToken ct, string source, string language)
    {
        var sw = Stopwatch.StartNew();
        if (fileName is null)
            return new SandboxResult(false, string.Empty, $"未找到 {language} 解释器", 1, sw.ElapsedMilliseconds);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (!_settings.NetworkEnabled)
            psi.Environment["AGENT_PLATFORM_SANDBOX_OFFLINE"] = "1";

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        string? launchError = null;
        int exitCode = -1;
        bool timedOut = false;
        string outStr = string.Empty;
        string errStr = string.Empty;
        try
        {
            if (!process.Start())
            {
                launchError = "无法启动进程";
            }
            else
            {
                // 事后挂接 OS 级隔离（Job Object 资源限额；AppContainer/Null 为 noop）。
                _isolation.Attach(process);
                var cap = await ProcessCaptureHelper.CaptureAsync(
                    process, timeoutSeconds, _settings.TimeoutSeconds, ct, language).ConfigureAwait(false);
                outStr = cap.Stdout;
                errStr = cap.Stderr;
                exitCode = cap.ExitCode;
                timedOut = cap.TimedOut;
            }
        }
        catch (Exception ex)
        {
            launchError = $"执行异常: {ex.Message}";
            exitCode = -1;
        }

        var truncatedOut = Truncate(outStr, _settings.MaxOutputBytes);
        var truncatedErr = Truncate(errStr, _settings.MaxOutputBytes);
        if (launchError != null)
            return new SandboxResult(false, truncatedOut, $"{launchError}\n{truncatedErr}", exitCode, sw.ElapsedMilliseconds);

        var success = !timedOut && exitCode == 0;
        var finalErr = timedOut ? $"执行超时（>{timeoutSeconds}s）\n{truncatedErr}" : truncatedErr;
        _logger.LogInformation("沙箱执行 {Lang} 完成：Success={Success} ExitCode={ExitCode} Duration={Duration}ms",
            language, success, exitCode, sw.ElapsedMilliseconds);
        return new SandboxResult(success, truncatedOut, finalErr, exitCode, sw.ElapsedMilliseconds);
    }

    private (string? cmd, string ext) ResolveInterpreter(string language)
    {
        var lang = (language ?? string.Empty).ToLowerInvariant();
        if (!_settings.AllowedLanguages.Contains(lang))
            return (null, ".txt");
        if (_settings.InterpreterPaths.TryGetValue(lang, out var custom) && !string.IsNullOrWhiteSpace(custom))
            return (custom, ExtFor(lang));
        return lang switch
        {
            "python" => ("python", ".py"),
            "javascript" => ("node", ".js"),
            "csscript" => ("cscript", ".csx"),
            _ => (null, ".txt")
        };
    }

    private static string ExtFor(string lang) => lang switch
    {
        "python" => ".py",
        "javascript" => ".js",
        "csscript" => ".csx",
        _ => ".txt"
    };

    private string? WriteTempFileOrNull(string code, string ext)
    {
        try
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"ap_sandbox_{Guid.NewGuid():N}{ext}");
            File.WriteAllText(tempFile, code ?? string.Empty, Encoding.UTF8);
            return tempFile;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "沙箱临时文件写入失败");
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* 临时文件清理失败不阻断 */ }
    }

    private static string EscapeArg(string? value) =>
        value is null ? string.Empty : $"\"{value.Replace("\"", "\\\"")}\"";

    private static string Truncate(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxBytes) return value;
        return value.Substring(0, maxBytes);
    }
}
