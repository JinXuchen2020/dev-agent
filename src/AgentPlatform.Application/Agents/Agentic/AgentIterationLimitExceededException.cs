namespace AgentPlatform.Application.Agents.Agentic;

/// <summary>
/// Thrown when the agentic control loop exceeds its configured maximum iteration count,
/// protecting against runaway loops and unbounded cost.
/// </summary>
public sealed class AgentIterationLimitExceededException : Exception
{
    /// <summary>Initializes a new instance with the exceeded limit.</summary>
    /// <param name="maxIterations">The configured maximum number of iterations.</param>
    public AgentIterationLimitExceededException(int maxIterations)
        : base($"Agentic control loop exceeded the maximum of {maxIterations} iterations.") { }
}
