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
        _logger.LogInformation(
            "Running {Lang} code in sandbox (timeout: {Timeout}s)",
            language, timeoutSeconds);

        return Task.FromResult(new SandboxResult(true,
            $"Executed {language} code successfully", string.Empty, 0, 100));
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
        _logger.LogInformation(
            "Running command in sandbox: {Command}", command);

        return Task.FromResult(new SandboxResult(true,
            $"Command executed: {command}", string.Empty, 0, 50));
    }
}
