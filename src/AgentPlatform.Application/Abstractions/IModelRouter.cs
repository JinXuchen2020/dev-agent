using AgentPlatform.Application.Routing.Services;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides an abstraction for routing chat requests to an appropriate model and returning the model's response.
/// </summary>
public interface IModelRouter
{
    /// <summary>
    /// Routes the specified request to the best available model candidate and returns the response.
    /// </summary>
    /// <param name="request">The routing request containing messages and optional preferred model.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task whose result contains the model's response.</returns>
    Task<ModelResponse> RouteAsync(RoutingRequest request, CancellationToken ct = default);

    /// <summary>
    /// Routes the specified request to the best available model candidate and streams the response
    /// token-by-token. Candidate selection and BYO/tenant isolation mirror <see cref="RouteAsync"/>;
    /// only the final answer text is streamed (intermediate tool-call turns are driven by <see cref="RouteAsync"/>).
    /// </summary>
    /// <param name="request">The routing request containing messages and optional preferred model.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>An asynchronous stream of response text chunks.</returns>
    IAsyncEnumerable<string> RouteStreamAsync(RoutingRequest request, CancellationToken ct = default);
}
