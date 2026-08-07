namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides operations for executing code and commands inside an isolated sandbox environment.
/// </summary>
public interface ICodeSandbox
{
    /// <summary>
    /// Executes the specified code in the sandbox using the given language.
    /// </summary>
    /// <param name="code">The source code to execute.</param>
    /// <param name="language">The programming language of the code (e.g., "python", "javascript").</param>
    /// <param name="timeoutSeconds">The maximum number of seconds to allow before the execution is aborted. Defaults to 30.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task whose result contains the outcome of the sandbox execution.</returns>
    Task<SandboxResult> RunCodeAsync(string code, string language,
        int timeoutSeconds = 30, CancellationToken ct = default);

    /// <summary>
    /// Executes the specified shell command inside the sandbox.
    /// </summary>
    /// <param name="command">The shell command to execute.</param>
    /// <param name="timeoutSeconds">The maximum number of seconds to allow before the command is aborted. Defaults to 30.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task whose result contains the outcome of the sandbox execution.</returns>
    Task<SandboxResult> RunCommandAsync(string command,
        int timeoutSeconds = 30, CancellationToken ct = default);
}

/// <summary>
/// Represents the outcome of a sandbox execution.
/// </summary>
/// <param name="Success">A value indicating whether the execution completed successfully.</param>
/// <param name="Stdout">The text written to standard output during execution.</param>
/// <param name="Stderr">The text written to standard error during execution.</param>
/// <param name="ExitCode">The process exit code returned by the sandbox.</param>
/// <param name="DurationMs">The total execution duration in milliseconds.</param>
/// <param name="IsolationStrength">
/// 本次执行实际使用的隔离强度（Strong=Docker 容器 / Weak=F11 进程级 / None=无 OS 级隔离）。
/// 末尾参数带默认值 <see cref="Abstractions.IsolationStrength.Weak"/>，既有 5 参构造调用全部向后兼容。
/// </param>
public record SandboxResult(
    bool Success,
    string Stdout,
    string Stderr,
    int ExitCode,
    long DurationMs,
    IsolationStrength IsolationStrength = IsolationStrength.Weak);
