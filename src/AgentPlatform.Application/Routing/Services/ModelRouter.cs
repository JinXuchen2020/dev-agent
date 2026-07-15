using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Application.Routing.Services;

/// <summary>
/// Implements <see cref="IModelRouter"/> by selecting candidate models in priority order,
/// reserving cost, and executing calls through a resilience pipeline with retry handling.
/// </summary>
public sealed class ModelRouter : IModelRouter
{
    private readonly IModelClient _modelClient;
    private readonly ICostController _costController;
    private readonly IResiliencePipelineProvider _pipelineProvider;
    private readonly ILogger<ModelRouter> _logger;
    private readonly RouterSettings _routerSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelRouter"/> class.
    /// </summary>
    /// <param name="modelClient">The client used to call the underlying model provider.</param>
    /// <param name="costController">The cost controller used to reserve and settle token costs.</param>
    /// <param name="pipelineProvider">The resilience pipeline provider that wraps model calls with retry and circuit-breaker policies.</param>
    /// <param name="logger">The logger used to record routing decisions and failures.</param>
    /// <param name="routerOptions">The options accessor providing router configuration, including candidate models.</param>
    public ModelRouter(
        IModelClient modelClient,
        ICostController costController,
        IResiliencePipelineProvider pipelineProvider,
        ILogger<ModelRouter> logger,
        IOptions<RouterSettings> routerOptions)
    {
        _modelClient = modelClient;
        _costController = costController;
        _pipelineProvider = pipelineProvider;
        _logger = logger;
        _routerSettings = routerOptions.Value;
    }

    /// <summary>
    /// Routes the specified request to the best available model candidate and returns the model's response.
    /// Candidates are tried in priority order; on budget exhaustion or retryable failure the router falls back
    /// to the next candidate. If all candidates fail, an <see cref="AllModelsFailedException"/> is thrown.
    /// </summary>
    /// <param name="request">The routing request containing messages and an optional preferred model.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task whose result contains the model's chat response.</returns>
    /// <exception cref="AllModelsFailedException">Thrown when every candidate model has failed or exceeded the budget.</exception>
    public async Task<ModelResponse> RouteAsync(RoutingRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Messages);

        var candidates = BuildCandidateList(request);

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            if (!_costController.TryReserve(candidate, _routerSettings.DefaultEstimatedTokens))
            {
                _logger.LogWarning("Skipping {ModelId}: over budget", candidate.ModelId);
                continue;
            }

            try
            {
                var response = await _pipelineProvider.ExecuteWithRetryAsync(
                    async (innerCt) => await _modelClient.ChatAsync(candidate.ModelId, request.Messages, innerCt), ct);

                _costController.SettleUsage(candidate, response.TokenUsage, _routerSettings.DefaultEstimatedTokens);
                return response;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _costController.ReleaseReservation(candidate, _routerSettings.DefaultEstimatedTokens);
                throw;
            }
            catch (Exception ex) when (IsRetryable(ex))
            {
                _costController.ReleaseReservation(candidate, _routerSettings.DefaultEstimatedTokens);
                _logger.LogWarning(ex, "Model {ModelId} failed, trying next", candidate.ModelId);
                continue;
            }
            catch (Exception ex)
            {
                _costController.ReleaseReservation(candidate, _routerSettings.DefaultEstimatedTokens);
                _logger.LogError(ex, "Model {ModelId} failed with non-retryable error", candidate.ModelId);
                throw;
            }
        }

        throw new AllModelsFailedException("All candidate models failed or exceeded budget");
    }

    private List<ModelCandidate> BuildCandidateList(RoutingRequest request)
    {
        var candidates = _routerSettings.Candidates
            .Select(c => new ModelCandidate(c.ModelId, c.Provider, c.Priority))
            .ToList();

        if (!string.IsNullOrEmpty(request.PreferredModel))
        {
            candidates = candidates
                .OrderByDescending(c =>
                    string.Equals(c.ModelId, request.PreferredModel, StringComparison.Ordinal) ? int.MaxValue : c.Priority)
                .ToList();
        }

        return candidates;
    }

    private static bool IsRetryable(Exception ex)
    {
        // OperationCanceledException covers Polly v8 timeout rejection
        // (user-cancellation OCE is caught earlier by when(ct.IsCancellationRequested))
        return ex is HttpRequestException or TimeoutException or TaskCanceledException or OperationCanceledException;
    }
}
