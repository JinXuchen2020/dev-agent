using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.ValueObjects;

namespace AgentPlatform.Application.Routing.Services;

/// <summary>
/// Implements <see cref="ICostController"/> to track per-tenant daily token spending against a configurable
/// budget, using per-provider pricing to reserve, settle, and release estimated costs. Also enforces a
/// per-tenant daily quota on platform-provided search calls.
/// BYO-key (tenant-owned) models/search bypass the budget/quota since cost is borne by the tenant.
/// </summary>
public sealed class CostController : ICostController
{
    private readonly object _lock = new();
    private readonly PricingSettings _pricing;
    private readonly ILogger<CostController> _logger;
    private readonly Money _perTenantDailyBudget;
    private readonly int _perTenantSearchQuota;
    private DateTime _lastResetDate = DateTime.UtcNow.Date;
    private readonly Dictionary<Guid, Money> _spentByTenant = new();
    private readonly Dictionary<Guid, int> _searchCountByTenant = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CostController"/> class.
    /// </summary>
    /// <param name="pricingOptions">The options accessor providing per-provider token pricing.</param>
    /// <param name="routerOptions">The options accessor providing the per-tenant daily budget limit.</param>
    /// <param name="searchOptions">The options accessor providing the per-tenant daily search quota.</param>
    /// <param name="logger">The logger used to record cost events and warnings.</param>
    public CostController(
        IOptions<PricingSettings> pricingOptions,
        IOptions<RouterSettings> routerOptions,
        IOptions<SearchSettings> searchOptions,
        ILogger<CostController> logger)
    {
        _pricing = pricingOptions.Value;
        _perTenantDailyBudget = new Money(routerOptions.Value.PerTenantDailyBudget);
        _perTenantSearchQuota = searchOptions.Value.PerTenantDailySearchQuota;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool TryReserve(ModelCandidate candidate, int estimatedTokens, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate.Provider);

        var estimatedCost = new Money(GetCostPerUnit(candidate.Provider) * estimatedTokens);

        lock (_lock)
        {
            ResetIfNewDay();

            var spent = GetOrZero(tenantId);
            if ((spent + estimatedCost) > _perTenantDailyBudget)
            {
                _logger.LogWarning(
                    "Tenant {TenantId} budget exceeded: spent {Spent}, estimated {Est}, budget {Budget}. Skipping {Provider}/{Model}.",
                    tenantId, spent.Amount, estimatedCost.Amount, _perTenantDailyBudget.Amount, candidate.Provider, candidate.ModelId);
                return false;
            }

            _spentByTenant[tenantId] = spent + estimatedCost;
            return true;
        }
    }

    /// <inheritdoc/>
    public void SettleUsage(ModelCandidate candidate, TokenUsage? actualTokenUsage, int reservedTokens, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var reservedCost = new Money(GetCostPerUnit(candidate.Provider) * reservedTokens);

        lock (_lock)
        {
            ResetIfNewDay();

            if (actualTokenUsage is null)
            {
                // Unknown actual usage — keep the reservation as the settled cost
                return;
            }

            var actualCost = new Money(GetCostPerUnit(candidate.Provider) * actualTokenUsage.TotalTokens);
            var delta = actualCost - reservedCost;
            _spentByTenant[tenantId] = GetOrZero(tenantId) + delta;

            _logger.LogInformation(
                "Cost settled for tenant {TenantId}: reserved {Reserved}, actual {Actual}, delta {Delta}. Total spent: {Total}",
                tenantId, reservedCost.Amount, actualTokenUsage?.TotalTokens ?? reservedTokens,
                delta.Amount, GetOrZero(tenantId).Amount);
        }
    }

    /// <inheritdoc/>
    public void ReleaseReservation(ModelCandidate candidate, int reservedTokens, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var reservedCost = new Money(GetCostPerUnit(candidate.Provider) * reservedTokens);

        lock (_lock)
        {
            ResetIfNewDay();
            var current = GetOrZero(tenantId) - reservedCost;
            _spentByTenant[tenantId] = current.Amount < 0 ? Money.Zero : current;

            _logger.LogInformation(
                "Released reservation for tenant {TenantId}: {Reserved}. Total spent: {Total}",
                tenantId, reservedCost.Amount, GetOrZero(tenantId).Amount);
        }
    }

    /// <inheritdoc/>
    public Money GetTodaySpent(Guid tenantId)
    {
        lock (_lock)
        {
            ResetIfNewDay();
            return GetOrZero(tenantId);
        }
    }

    /// <inheritdoc/>
    public bool TryRecordSearch(Guid tenantId)
    {
        lock (_lock)
        {
            ResetIfNewDay();
            var count = _searchCountByTenant.GetValueOrDefault(tenantId, 0);
            if (count >= _perTenantSearchQuota)
            {
                _logger.LogWarning(
                    "Tenant {TenantId} platform search quota exhausted: {Count}/{Quota}.",
                    tenantId, count, _perTenantSearchQuota);
                return false;
            }

            _searchCountByTenant[tenantId] = count + 1;
            return true;
        }
    }

    private Money GetOrZero(Guid tenantId) =>
        _spentByTenant.TryGetValue(tenantId, out var spent) ? spent : Money.Zero;

    private decimal GetCostPerUnit(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        if (_pricing.CostPerMillionTokens.TryGetValue(provider, out var perMillion))
            return perMillion / RoutingConstants.CostPerMillionDivisor;

        _logger.LogWarning(
            "Provider {Provider} not found in pricing table; using default cost {Default}/token",
            provider, RoutingConstants.DefaultCostPerUnit);
        return RoutingConstants.DefaultCostPerUnit;
    }

    private void ResetIfNewDay()
    {
        var today = DateTime.UtcNow.Date;
        if (today > _lastResetDate)
        {
            _logger.LogInformation(
                "Daily budget/quota reset: previous date {PreviousDate} -> {NewDate}",
                _lastResetDate, today);
            _spentByTenant.Clear();
            _searchCountByTenant.Clear();
            _lastResetDate = today;
        }
    }
}
