using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Sandbox;

/// <summary>
/// 进程输出捕获 + 超时杀 + 退出码的共享实现。被 <see cref="ProcessCodeSandbox"/> 的常规路径与
/// <see cref="AppContainerSandboxIsolation"/> 的 AppContainer 路径共同复用，避免重复。
/// 语义与 F5/F9 既有行为一致：超时（或调用方取消触发的 OperationCanceledException）判定为 timedOut 并杀进程树。
/// </summary>
internal static class ProcessCaptureHelper
{
    public static async Task<(string Stdout, string Stderr, int ExitCode, bool TimedOut)> CaptureAsync(
        Process process, int timeoutSeconds, int fallbackTimeoutSeconds, CancellationToken ct, string language)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

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
            TryKill(process);
        }

        // 给输出流一点时间 flush
        await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
        exitCode = process.HasExited ? process.ExitCode : -1;
        return (stdout.ToString(), stderr.ToString(), exitCode, timedOut);
    }

    public static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { /* 进程可能已退出 */ }
    }
}
