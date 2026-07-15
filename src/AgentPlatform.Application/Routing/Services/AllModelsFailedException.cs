namespace AgentPlatform.Application.Routing.Services;

/// <summary>
/// Thrown when all candidate models have either failed or exceeded the daily budget,
/// and no model is available to handle the routing request.
/// </summary>
public sealed class AllModelsFailedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AllModelsFailedException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public AllModelsFailedException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="AllModelsFailedException"/> class with a specified error message
    /// and a reference to the inner exception that caused this error.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public AllModelsFailedException(string message, Exception innerException) : base(message, innerException) { }
}
