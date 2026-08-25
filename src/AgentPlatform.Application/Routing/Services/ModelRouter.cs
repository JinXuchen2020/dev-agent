using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        // F31: fail with an actionable message instead of the generic AllModelsFailedException —
        // an empty candidate list means nothing is configured anywhere, not that models failed.
        if (candidates.Count == 0)
            throw new ModelNotConfiguredException(tenantId);

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
                    async (innerCt) => await activeClient.ChatAsync(candidate.ModelId, request.Messages, request.Tools, ct: innerCt), ct);

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

    /// <summary>
    /// Streams the routed response token-by-token, mirroring <see cref="RouteAsync"/> for candidate
    /// selection, BYO/tenant isolation and budget reservation. The first successfully-connected candidate
    /// is streamed; retryable failures fall through to the next candidate.
    /// </summary>
    public IAsyncEnumerable<string> RouteStreamAsync(RoutingRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Messages);

        // C# 不允许在含 catch 的 try 体内 yield return（CS1626），因此把"含 catch 的候选回退逻辑"
        // 放到独立的 PumpStreamAsync（无 yield），这里只从 Channel 读取，方法本身无 catch 因而合法。
        var channel = System.Threading.Channels.Channel.CreateUnbounded<string>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleWriter = true });

        _ = PumpStreamAsync(request, channel.Writer, ct);

        return channel.Reader.ReadAllAsync(ct);
    }

    /// <summary>
    /// 将流式候选回退逻辑（含 catch 的 fallback / budget 处理）推入 Channel。
    /// 独立方法内无 yield return，故不受 CS1626 约束。
    /// </summary>
    private async Task PumpStreamAsync(
        RoutingRequest request,
        System.Threading.Channels.ChannelWriter<string> writer,
        CancellationToken ct)
    {
        try
        {
            var tenantId = _tenantProvider.GetTenantId();
        var tenantResolutions = await _modelResolver.ResolveAsync(tenantId, ct);

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

        var platformCandidates = _platformModelProvider.GetCandidates();
        var candidates = byoCandidates.Count > 0
            ? byoCandidates.Concat(platformCandidates).ToList()
            : platformCandidates;

        // F31: mirror of the RouteAsync guard — empty candidates means nothing configured anywhere.
        if (candidates.Count == 0)
            throw new ModelNotConfiguredException(tenantId);

        if (byoCandidates.Count == 0)
            _logger.LogInformation("Streaming for tenant {TenantId} via platform models", tenantId);
        else
            _logger.LogInformation("Streaming for tenant {TenantId} via {Count} tenant BYO model client(s)", tenantId, byoClients.Count);

        var candidateList = BuildCandidateList(request, candidates);

        Exception? lastError = null;
        foreach (var candidate in candidateList)
        {
            ct.ThrowIfCancellationRequested();

            var usePlatformBudget = !byoClients.ContainsKey(candidate.ModelId);
            var activeClient = usePlatformBudget ? _platformModelClient : byoClients[candidate.ModelId];

            if (usePlatformBudget && !_costController.TryReserve(candidate, _routerSettings.DefaultEstimatedTokens, tenantId))
            {
                _logger.LogWarning("Skipping {ModelId}: over tenant budget", candidate.ModelId);
                continue;
            }

            IAsyncEnumerator<string>? enumerator = null;
            try
            {
                // 流式路径没有 Polly 管道兜底（RouteAsync 有超时策略，这里手动对齐）。
                // TimeoutSeconds <= 0（默认）表示禁用单次调用超时，让长生成一直跑到完成；
                // 设正数时才用 linked CTS 做超时保护（防模型半开流令 MoveNextAsync 无限挂起）。
                CancellationToken effectiveCt = ct;
                using var timeoutCts = _routerSettings.TimeoutSeconds > 0
                    ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                    : null;
                if (timeoutCts is not null)
                {
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(_routerSettings.TimeoutSeconds));
                    effectiveCt = timeoutCts.Token;
                }

                enumerator = activeClient.ChatStreamAsync(candidate.ModelId, request.Messages, effectiveCt)
                    .GetAsyncEnumerator(effectiveCt);
                while (await enumerator.MoveNextAsync())
                {
                    await writer.WriteAsync(enumerator.Current, effectiveCt);
                }

                if (usePlatformBudget) _costController.SettleUsage(candidate, null, _routerSettings.DefaultEstimatedTokens, tenantId);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (usePlatformBudget) _costController.ReleaseReservation(candidate, _routerSettings.DefaultEstimatedTokens, tenantId);
                throw;
            }
            catch (Exception ex) when (IsRetryable(ex))
            {
                lastError = ex;
                if (usePlatformBudget) _costController.ReleaseReservation(candidate, _routerSettings.DefaultEstimatedTokens, tenantId);
                _logger.LogWarning(ex, "Streaming model {ModelId} failed, trying next", candidate.ModelId);
                continue;
            }
            catch (Exception ex)
            {
                if (usePlatformBudget) _costController.ReleaseReservation(candidate, _routerSettings.DefaultEstimatedTokens, tenantId);
                _logger.LogError(ex, "Streaming model {ModelId} failed with non-retryable error", candidate.ModelId);
                throw;
            }
            finally
            {
                if (enumerator is not null)
                {
                    await enumerator.DisposeAsync();
                }
            }
        }

            throw lastError is null
                ? new AllModelsFailedException("All candidate models failed or exceeded budget")
                : new AllModelsFailedException("All candidate models failed or exceeded budget", lastError);
        }
        catch (Exception ex)
        {
            // 把任何失败（含不可重试异常 / 全部候选失败）写入 Channel，由 RouteStreamAsync 的读取方传播。
            writer.TryComplete(ex);
        }
        finally
        {
            // 正常完成路径（L224 return）此前从不关闭 Channel，导致 ReadAllAsync 永不结束、
            // 调用方读完所有 answer_delta 后仍等待，done 事件永远发不出（SSE 流挂起）。
            // TryComplete 幂等：异常已完成时此处为 no-op。
            writer.TryComplete();
        }
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
