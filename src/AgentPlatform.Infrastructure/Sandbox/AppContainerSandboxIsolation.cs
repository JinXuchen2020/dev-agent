using System.Diagnostics;
using System.IO;
using System.Text;
using AgentPlatform.Application.Abstractions;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Sandbox;

/// <summary>
/// Windows AppContainer 隔离：在真实阻断出网的 AppContainer 内启动解释器执行用户代码（代码经 stdin 管道喂入，避免临时文件可读性问题），
/// 并叠加 Job Object 资源限额。解释器不可在容器内加载时（缺主机 ACL 准备）透明回退到常规启动（由 <see cref="ProcessCodeSandbox"/> 兜底面）。
/// 仅用于 <c>OsIsolation=AppContainer/Full</c> 且平台为 Windows。
/// </summary>
internal sealed class AppContainerSandboxIsolation : ISandboxIsolation
{
    public bool CanLaunch => true;

    public IsolationStrength Strength => IsolationStrength.Weak;

    private readonly ILogger<AppContainerSandboxIsolation> _logger;
    private readonly SandboxSettings _settings;

    public AppContainerSandboxIsolation(ILogger<AppContainerSandboxIsolation> logger, IOptions<SandboxSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public bool Attach(Process process) => false;

    public async Task<SandboxResult?> TryLaunchAsync(
        string fileName, string arguments, int timeoutSeconds, CancellationToken ct, string source, string language)
    {
        if (fileName is null)
            return null;
        if (!TryGetCmds(language, out var probeCmd, out var runCmd))
            return null; // csscript 等需文件访问，回退常规路径

        using var ac = new WindowsAppContainer(_logger);
        if (!ac.TryCreateProfile())
            return null;

        // 探针：确认解释器可在 AppContainer 内加载运行（主机已准备 ALL APPLICATION PACKAGES 读 ACL）。
        var probe = ac.Launch(fileName, probeCmd);
        if (!probe.Success)
            return null;
        try { probe.StdinWrite?.Dispose(); } catch { }
        var (pOut, _, pExit, _) = await CapturePipeAsync(probe.Process!, probe.Stdout!, probe.Stderr!,
            timeoutSeconds, _settings.TimeoutSeconds, ct).ConfigureAwait(false);
        probe.Dispose();
        if (pExit != 0 || !pOut.Trim().Contains("ok"))
        {
            _logger.LogWarning("AppContainer 解释器探针失败（ExitCode={Exit}），回退常规启动 + 环境标记缓解项", pExit);
            return null;
        }

        // 真实执行：代码经 stdin 喂入，网络被 AppContainer 真实阻断。
        var run = ac.Launch(fileName, runCmd);
        if (!run.Success)
            return null;
        try
        {
            await WriteStdinAsync(run.StdinWrite!, source).ConfigureAwait(false);
            using var job = new WindowsJobObject(_settings.MemoryLimitBytes, _settings.CpuRatePercent,
                _settings.MaxProcessCount, _logger);
            job.Assign(run.Process!);

            var sw = Stopwatch.StartNew();
            var (outStr, errStr, exit, timedOut) = await CapturePipeAsync(run.Process!, run.Stdout!, run.Stderr!,
                timeoutSeconds, _settings.TimeoutSeconds, ct).ConfigureAwait(false);
            sw.Stop();

            var success = !timedOut && exit == 0;
            var finalErr = timedOut ? $"执行超时（>{timeoutSeconds}s）\n{errStr}" : errStr;
            _logger.LogInformation("AppContainer 沙箱执行 {Lang} 完成：Success={Success} ExitCode={ExitCode} Duration={Duration}ms",
                language, success, exit, sw.ElapsedMilliseconds);
            return new SandboxResult(success, Truncate(outStr, _settings.MaxOutputBytes),
                Truncate(finalErr, _settings.MaxOutputBytes), exit, sw.ElapsedMilliseconds);
        }
        finally
        {
            run.Dispose();
        }
    }

    private static bool TryGetCmds(string language, out string probeCmd, out string runCmd)
    {
        var lang = (language ?? string.Empty).ToLowerInvariant();
        switch (lang)
        {
            case "python":
                probeCmd = "-c \"print('ok')\"";
                runCmd = "-";
                return true;
            case "javascript":
                probeCmd = "-e \"console.log('ok')\"";
                runCmd = "-";
                return true;
            default:
                probeCmd = string.Empty;
                runCmd = string.Empty;
                return false;
        }
    }

    private static async Task WriteStdinAsync(SafeFileHandle stdinWrite, string source)
    {
        try
        {
            await using var writer = new StreamWriter(new FileStream(stdinWrite, FileAccess.Write), Encoding.UTF8, leaveOpen: true);
            await writer.WriteAsync(source ?? string.Empty).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            try { stdinWrite.Dispose(); } catch { }
        }
    }

    private static async Task<(string Stdout, string Stderr, int ExitCode, bool TimedOut)> CapturePipeAsync(
        Process process, Stream stdout, Stream stderr, int timeoutSeconds, int fallbackTimeoutSeconds, CancellationToken ct)
    {
        var outTask = Task.Run(() => ReadToEnd(stdout));
        var errTask = Task.Run(() => ReadToEnd(stderr));
        int exitCode = -1;
        bool timedOut = false;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds <= 0 ? fallbackTimeoutSeconds : timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            ProcessCaptureHelper.TryKill(process);
        }

        await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
        exitCode = process.HasExited ? process.ExitCode : -1;
        var outStr = await outTask.ConfigureAwait(false);
        var errStr = await errTask.ConfigureAwait(false);
        return (outStr, errStr, exitCode, timedOut);
    }

    private static string ReadToEnd(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Truncate(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxBytes) return value;
        return value.Substring(0, maxBytes);
    }
}
