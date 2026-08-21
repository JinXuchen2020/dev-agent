using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Application.Routing.Services;

/// <summary>
/// Implements <see cref="IModelRouter"/> by selecting candidate models in priority order,
/// reserving cost, and executing calls through a resilience pipeline with retry handling.
/// Candidate selection merges the tenant's BYO model client (when configured) with the
/// platform model catalog: a tenant with an active credential is served by its own key, isolated
/// from other tenants; otherwise the platform (operator-configured) models are used and billed per tenant.
/// </summary>
public sealed class ModelRouter : IModelRouter
{
    private readonly IModelClient _platformModelClient;
    private readonly ITenantModelClientResolver _modelResolver;
    private readonly ITenantProvider _tenantProvider;
    private readonly IPlatformModelProvider _platformModelProvider;
    private readonly ICostController _costController;
    private readonly IResiliencePipelineProvider _pipelineProvider;
    private readonly ILogger<ModelRouter> _logger;
    private readonly RouterSettings _routerSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelRouter"/> class.
    /// </summary>
    public ModelRouter(
        IModelClient platformModelClient,
        ITenantModelClientResolver modelResolver,
        ITenantProvider tenantProvider,
        IPlatformModelProvider platformModelProvider,
        ICostController costController,
        IResiliencePipelineProvider pipelineProvider,
        ILogger<ModelRouter> logger,
        IOptions<RouterSettings> routerOptions)
    {
        _platformModelClient = platformModelClient;
        _modelResolver = modelResolver;
        _tenantProvider = tenantProvider;
        _platformModelProvider = platformModelProvider;
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
    public async Task<ModelResponse> RouteAsync(RoutingRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Messages);

        var tenantId = _tenantProvider.GetTenantId();
        var tenantResolutions = await _modelResolver.ResolveAsync(tenantId, ct);

        // Map each BYO candidate model id to the client built from its own credential key, so that
        // multiple BYO models (potentially different providers) each route through their own key.
        var byoClients = new Dictionary<string, IModelClient>(StringComparer.Ordinal);
        var byoCandidates = new List<ModelCandidate>();
        foreach (var resolution in tenantResolutions)
        {
            foreach (var candidate in resolution.Candidates)
            {
                byoClients[candidate.ModelId] = resolution.Client;
                byoCandidates.Add(candidate);
            }
        }

        // Tenant with at least one active BYO model credential => BYO candidates take priority (no platform budget).
        // Otherwise fall back to platform models, billed per tenant via the cost controller.
        var platformCandidates = _platformModelProvider.GetCandidates();
        var candidates = byoCandidates.Count > 0
            ? byoCandidates.Concat(platformCandidates).ToList()
            : platformCandidates;

        if (byoCandidates.Count == 0)
            _logger.LogInformation("Routing for tenant {TenantId} via platform models", tenantId);
        else
            _logger.LogInformation("Routing for tenant {TenantId} via {Count} tenant BYO model client(s)", tenantId, byoClients.Count);

        var candidateList = BuildCandidateList(request, candidates);

        foreach (var candidate in candidateList)
        {
            ct.ThrowIfCancellationRequested();

            // Per-candidate client + budget decision: BYO models use their own key (no platform budget);
            // platform models use the platform client and are billed per tenant.
            var usePlatformBudget = !byoClients.ContainsKey(candidate.ModelId);
            var activeClient = usePlatformBudget ? _platformModelClient : byoClients[candidate.ModelId];

            if (usePlatformBudget && !_costController.TryReserve(candidate, _routerSettings.DefaultEstimatedTokens, tenantId))
            {
                _logger.LogWarning("Skipping {ModelId}: over tenant budget", candidate.ModelId);
                continue;
            }

            try
            {
                var response = await _pipelineProvider.ExecuteWithRetryAsync(
                    async (innerCt) => await activeClient.ChatAsync(candidate.ModelId, request.Messages, ct: innerCt), ct);

                if (usePlatformBudget)
                    _costController.SettleUsage(candidate, response.TokenUsage, _routerSettings.DefaultEstimatedTokens, tenantId);
                return response;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (usePlatformBudget) _costController.ReleaseReservation(candidate, _routerSettings.DefaultEstimatedTokens, tenantId);
                throw;
            }
            catch (Exception ex) when (IsRetryable(ex))
            {
                if (usePlatformBudget) _costController.ReleaseReservation(candidate, _routerSettings.DefaultEstimatedTokens, tenantId);
                _logger.LogWarning(ex, "Model {ModelId} failed, trying next", candidate.ModelId);
                continue;
            }
            catch (Exception ex)
            {
                if (usePlatformBudget) _costController.ReleaseReservation(candidate, _routerSettings.DefaultEstimatedTokens, tenantId);
                _logger.LogError(ex, "Model {ModelId} failed with non-retryable error", candidate.ModelId);
                throw;
            }
        }

        throw new AllModelsFailedException("All candidate models failed or exceeded budget");
    }

    private List<ModelCandidate> BuildCandidateList(RoutingRequest request, IReadOnlyList<ModelCandidate> candidates)
    {
        var list = candidates.ToList();

        if (!string.IsNullOrEmpty(request.PreferredModel))
        {
            list = list
                .OrderByDescending(c =>
                    string.Equals(c.ModelId, request.PreferredModel, StringComparison.Ordinal) ? int.MaxValue : c.Priority)
                .ToList();
        }

        return list;
    }

    private static bool IsRetryable(Exception ex)
    {
        // OperationCanceledException covers Polly v8 timeout rejection
        // (user-cancellation OCE is caught earlier by when(ct.IsCancellationRequested))
        return ex is HttpRequestException or TimeoutException or TaskCanceledException or OperationCanceledException;
    }
}
