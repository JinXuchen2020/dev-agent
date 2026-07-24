using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Sandbox;

/// <summary>
/// Executes code and commands inside an isolated Docker container sandbox.
/// </summary>
internal sealed class DockerCodeSandbox : ICodeSandbox
{
    private readonly ILogger<DockerCodeSandbox> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerCodeSandbox"/> class.
    /// </summary>
    /// <param name="logger">The logger used to capture sandbox execution telemetry.</param>
    public DockerCodeSandbox(ILogger<DockerCodeSandbox> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Runs the supplied source code in the sandbox and returns the execution result.
    /// </summary>
    /// <param name="code">The source code to execute.</param>
    /// <param name="language">The programming language of the source code.</param>
    /// <param name="timeoutSeconds">The maximum number of seconds to allow before terminating execution.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a <see cref="SandboxResult"/> describing the outcome.</returns>
    public Task<SandboxResult> RunCodeAsync(string code, string language,
        int timeoutSeconds = 30, CancellationToken ct = default)
    {
        throw new InvalidOperationException(
            "DockerCodeSandbox 尚未接入真实 Docker 运行时（本构建未引用 Docker SDK 且运行环境无 Docker 守护进程）。" +
            "请使用 Sandbox:Provider=Process（默认，进程级真实执行）以获得真实副作用。如需 Docker 隔离，请在 Phase 6 接入 Docker.DotNet 并实现真实容器执行。");
    }

    /// <summary>
    /// Runs the supplied shell command in the sandbox and returns the execution result.
    /// </summary>
    /// <param name="command">The shell command to execute.</param>
    /// <param name="timeoutSeconds">The maximum number of seconds to allow before terminating execution.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a <see cref="SandboxResult"/> describing the outcome.</returns>
    public Task<SandboxResult> RunCommandAsync(string command,
        int timeoutSeconds = 30, CancellationToken ct = default)
    {
        throw new InvalidOperationException(
            "DockerCodeSandbox 尚未接入真实 Docker 运行时（本构建未引用 Docker SDK 且运行环境无 Docker 守护进程）。" +
            "请使用 Sandbox:Provider=Process（默认，进程级真实执行）以获得真实副作用。如需 Docker 隔离，请在 Phase 6 接入 Docker.DotNet 并实现真实容器执行。");
    }
}
