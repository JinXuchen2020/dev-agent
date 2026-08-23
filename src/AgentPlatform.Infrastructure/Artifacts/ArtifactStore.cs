using System.IO;
using System.Runtime.CompilerServices;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Artifacts;

/// <summary>
/// Persists a finished run's temp workspace into <c>{ContentRoot}/data/agent-runs/{runId}/</c>
/// so generated files survive process restarts and can be listed / previewed / downloaded by the UI.
/// </summary>
internal sealed class ArtifactStore : IArtifactStore
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ArtifactStore> _logger;

    public ArtifactStore(IHostEnvironment environment, ILogger<ArtifactStore> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ArtifactEntry>> SnapshotAsync(
        Guid runId, string workspaceRoot, CancellationToken ct = default)
    {
        var entries = new List<ArtifactEntry>();
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            return entries;

        var destRoot = Path.Combine(_environment.ContentRootPath, "data", "agent-runs", runId.ToString("N"));
        try
        {
            Directory.CreateDirectory(destRoot);
            foreach (var src in Directory.EnumerateFiles(workspaceRoot, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(workspaceRoot, src).Replace('\\', '/');
                var dest = Path.Combine(destRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                await FileCopyWithRetryAsync(src, dest, ct);
                var info = new FileInfo(dest);
                entries.Add(new ArtifactEntry(rel, info.Length));
            }
            _logger.LogInformation("Run {RunId} 产物快照完成：{Count} 个文件 → {Dest}", runId, entries.Count, destRoot);
        }
        catch (Exception ex)
        {
            // 产物快照是「尽力而为」的增强能力：任何失败都不应阻断 run 正常结束（done 事件仍会发出）。
            _logger.LogWarning(ex, "Run {RunId} 产物快照失败（不影响 run 结果）", runId);
            entries.Clear();
        }

        return entries;
    }

    private static async Task FileCopyWithRetryAsync(string src, string dest, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await CopyFileAsync(src, dest, ct);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                await Task.Delay(50 * attempt, ct);
            }
        }
    }

    // 显式用 FileShare.Read 读源，避免 Windows 上文件仍被沙箱子进程占用时的 SharingViolation。
    private static async Task CopyFileAsync(string src, string dest, CancellationToken ct)
    {
        using var source = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        using var target = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await source.CopyToAsync(target, 81920, ct);
    }
}
