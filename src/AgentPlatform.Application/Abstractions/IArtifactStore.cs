namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// A single file produced by an agent run, made available for in-platform preview / download.
/// </summary>
/// <param name="Path">Relative POSIX-style path inside the run's artifact folder (e.g. <c>index.html</c>).</param>
/// <param name="Size">File size in bytes.</param>
public record ArtifactEntry(string Path, long Size);

/// <summary>
/// Persists the contents of a finished run's temp workspace into a durable, run-scoped folder so the
/// generated files survive process restarts and can be listed / previewed / downloaded by the platform.
/// </summary>
public interface IArtifactStore
{
    /// <summary>
    /// Copies every file under <paramref name="workspaceRoot"/> into <c>data/agent-runs/{runId}/</c>
    /// and returns the resulting artifact manifest. No-op (empty list) when the root is empty/unavailable.
    /// </summary>
    Task<IReadOnlyList<ArtifactEntry>> SnapshotAsync(Guid runId, string workspaceRoot, CancellationToken ct = default);
}
