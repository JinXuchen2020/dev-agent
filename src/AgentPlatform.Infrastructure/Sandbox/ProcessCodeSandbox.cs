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
/// 不依赖 Docker，可在本沙箱运行与验证；隔离性弱于容器（网络无法在 OS 层强制隔离，
/// 仅设 <c>AGENT_PLATFORM_SANDBOX_OFFLINE</c> 环境标记）。
/// </summary>
internal sealed class ProcessCodeSandbox : ICodeSandbox
{
    private readonly ILogger<ProcessCodeSandbox> _logger;
    private readonly SandboxSettings _settings;

    public ProcessCodeSandbox(ILogger<ProcessCodeSandbox> logger, IOptions<SandboxSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<SandboxResult> RunCodeAsync(string code, string language,
        int timeoutSeconds = 30, CancellationToken ct = default)
    {
        var (cmd, ext) = ResolveInterpreter(language);
        if (cmd is null)
            return new SandboxResult(false, string.Empty, $"未找到 {language} 解释器或未授权", 1, 0);

        var tempFile = WriteTempFileOrNull(code, ext);
        if (tempFile is null)
            return new SandboxResult(false, string.Empty, "临时文件写入失败", 1, 0);

        try
        {
            return await RunProcessAsync(cmd, EscapeArg(tempFile), timeoutSeconds, ct, code, language);
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
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        int exitCode = -1;
        bool timedOut = false;
        string? launchError = null;
        try
        {
            if (!process.Start())
            {
                launchError = "无法启动进程";
            }
            else
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds <= 0 ? _settings.TimeoutSeconds : timeoutSeconds));
                try
                {
                    await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    timedOut = true;
                    TryKill(process);
                }

                // 给输出流一点时间 flush
                await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
                exitCode = process.HasExited ? process.ExitCode : -1;
            }
        }
        catch (Exception ex)
        {
            launchError = $"执行异常: {ex.Message}";
            exitCode = -1;
        }

        var outStr = Truncate(stdout.ToString(), _settings.MaxOutputBytes);
        var errStr = Truncate(stderr.ToString(), _settings.MaxOutputBytes);
        if (launchError != null)
            return new SandboxResult(false, outStr, $"{launchError}\n{errStr}", exitCode, sw.ElapsedMilliseconds);

        var success = !timedOut && exitCode == 0;
        var finalErr = timedOut ? $"执行超时（>{timeoutSeconds}s）\n{errStr}" : errStr;
        _logger.LogInformation("沙箱执行 {Lang} 完成：Success={Success} ExitCode={ExitCode} Duration={Duration}ms",
            language, success, exitCode, sw.ElapsedMilliseconds);
        return new SandboxResult(success, outStr, finalErr, exitCode, sw.ElapsedMilliseconds);
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

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { /* 进程可能已退出 */ }
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
