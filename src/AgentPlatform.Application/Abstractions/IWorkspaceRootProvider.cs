namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Exposes the per-run isolated workspace directory that the workspace tools
/// (write_file / run_command / …) operate in, so the orchestrator can snapshot
/// its contents into a persistent artifact store once a run finishes.
/// </summary>
public interface IWorkspaceRootProvider
{
    /// <summary>Absolute path of the temp workspace root, created lazily on first access.</summary>
    string WorkspaceRoot { get; }
}
