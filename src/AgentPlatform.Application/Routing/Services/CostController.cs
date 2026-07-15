using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.ValueObjects;

namespace AgentPlatform.Application.Routing.Services;

/// <summary>
/// Implements <see cref="ICostController"/> to track daily token spending against a configurable budget,
/// using per-provider pricing to reserve, settle, and release estimated costs.
/// </summary>
public sealed class CostController : ICostController
{
    private Money _dailyBudget;
    private Money _todaySpent = Money.Zero;
    private DateTime _lastResetDate = DateTime.UtcNow.Date;
    private readonly object _lock = new();
    private readonly PricingSettings _pricing;
    private readonly ILogger<CostController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CostController"/> class.
    /// </summary>
    /// <param name="pricingOptions">The options accessor providing per-provider token pricing.</param>
    /// <param name="routerOptions">The options accessor providing the daily budget limit.</param>
    /// <param name="logger">The logger used to record cost events and warnings.</param>
    public CostController(
        IOptions<PricingSettings> pricingOptions,
        IOptions<RouterSettings> routerOptions,
        ILogger<CostController> logger)
    {
        _dailyBudget = new Money(routerOptions.Value.DailyBudget);
        _pricing = pricingOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to reserve an estimated token cost for the given candidate against the daily budget.
    /// </summary>
    /// <param name="candidate">The model candidate for which to reserve cost.</param>
    /// <param name="estimatedTokens">The estimated number of tokens for the upcoming request.</param>
    /// <returns><c>true</c> if the reservation was accepted; <c>false</c> if the budget would be exceeded.</returns>
    public bool TryReserve(ModelCandidate candidate, int estimatedTokens)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate.Provider);

        var estimatedCost = new Money(GetCostPerUnit(candidate.Provider) * estimatedTokens);

        lock (_lock)
        {
            ResetIfNewDay();

            if ((_todaySpent + estimatedCost) > _dailyBudget)
            {
                _logger.LogWarning(
                    "Budget exceeded: spent {Spent}, estimated {Est}, budget {Budget}. Skipping {Provider}/{Model}.",
                    _todaySpent.Amount, estimatedCost.Amount, _dailyBudget.Amount, candidate.Provider, candidate.ModelId);
                return false;
            }

            _todaySpent += estimatedCost;
            return true;
        }
    }

    /// <summary>
    /// Reconciles a previously reserved cost against the actual token usage after the request completes.
    /// </summary>
    /// <param name="candidate">The model candidate whose usage is being settled.</param>
    /// <param name="actualTokenUsage">The actual token usage reported by the model, or <c>null</c> if unknown.</param>
    /// <param name="reservedTokens">The number of tokens that were originally reserved.</param>
    public void SettleUsage(ModelCandidate candidate, TokenUsage? actualTokenUsage, int reservedTokens)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var reservedCost = new Money(GetCostPerUnit(candidate.Provider) * reservedTokens);

        Money totalAfterSettle;
        Money delta;

        lock (_lock)
        {
            ResetIfNewDay();

            if (actualTokenUsage is null)
            {
                // Unknown actual usage — keep the reservation as the settled cost
                delta = Money.Zero;
                totalAfterSettle = _todaySpent;
            }
            else
            {
                var actualCost = new Money(GetCostPerUnit(candidate.Provider) * actualTokenUsage.TotalTokens);
                delta = actualCost - reservedCost;
                _todaySpent += delta;
                totalAfterSettle = _todaySpent;
            }

            _logger.LogInformation(
                "Cost settled: reserved {Reserved}, actual {Actual}, delta {Delta}. Total spent: {Total}",
                reservedCost.Amount,
                actualTokenUsage?.TotalTokens ?? reservedTokens,
                delta.Amount,
                totalAfterSettle.Amount);
        }
    }

    /// <summary>
    /// Releases a previously reserved token cost back to the daily budget when the request did not complete.
    /// </summary>
    /// <param name="candidate">The model candidate whose reservation should be released.</param>
    /// <param name="reservedTokens">The number of tokens that were originally reserved.</param>
    public void ReleaseReservation(ModelCandidate candidate, int reservedTokens)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var reservedCost = new Money(GetCostPerUnit(candidate.Provider) * reservedTokens);

        Money totalAfterRelease;

        lock (_lock)
        {
            ResetIfNewDay();
            _todaySpent -= reservedCost;
            if (_todaySpent.Amount < 0)
            {
                _logger.LogWarning(
                    "ReleaseReservation made _todaySpent negative ({Negative}), clamping to zero. Reserved: {Reserved}. This indicates a reservation tracking bug.",
                    _todaySpent.Amount, reservedCost.Amount);
                _todaySpent = Money.Zero;
            }
            totalAfterRelease = _todaySpent;

            _logger.LogInformation(
                "Released reservation: {Reserved}. Total spent: {Total}",
                reservedCost.Amount, totalAfterRelease.Amount);
        }
    }

    /// <summary>
    /// Returns the total amount spent today.
    /// </summary>
    /// <returns>A <see cref="Money"/> value representing today's cumulative spend.</returns>
    public Money GetTodaySpent()
    {
        lock (_lock)
        {
            ResetIfNewDay();
            return _todaySpent;
        }
    }

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
                "Daily budget reset: previous spent {Previous}, date {PreviousDate} -> {NewDate}",
                _todaySpent.Amount, _lastResetDate, today);
            _todaySpent = Money.Zero;
            _lastResetDate = today;
        }
    }
}
