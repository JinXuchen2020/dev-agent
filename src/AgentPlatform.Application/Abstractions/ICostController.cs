using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.ValueObjects;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides operations for reserving, settling, and releasing estimated token costs against a per-tenant daily budget.
/// Platform-provided (built-in) models are billed per tenant; BYO-key (tenant-owned) models are not subject to the budget.
/// Also tracks per-tenant search-call quota for platform-provided search.
/// </summary>
public interface ICostController
{
    /// <summary>
    /// Attempts to reserve an estimated token cost for the given candidate against the tenant's daily budget.
    /// </summary>
    /// <param name="candidate">The model candidate for which to reserve cost.</param>
    /// <param name="estimatedTokens">The estimated number of tokens for the upcoming request.</param>
    /// <param name="tenantId">The tenant whose budget is charged (platform models only).</param>
    /// <returns><c>true</c> if the reservation was accepted; <c>false</c> if the tenant budget would be exceeded.</returns>
    bool TryReserve(ModelCandidate candidate, int estimatedTokens, Guid tenantId);

    /// <summary>
    /// Reconciles a previously reserved cost against the actual token usage after the request completes.
    /// </summary>
    /// <param name="candidate">The model candidate whose usage is being settled.</param>
    /// <param name="actualTokenUsage">The actual token usage reported by the model, or <c>null</c> if unknown.</param>
    /// <param name="reservedTokens">The number of tokens that were originally reserved.</param>
    /// <param name="tenantId">The tenant whose budget is settled.</param>
    void SettleUsage(ModelCandidate candidate, TokenUsage? actualTokenUsage, int reservedTokens, Guid tenantId);

    /// <summary>
    /// Releases a previously reserved token cost back to the tenant's daily budget when the request did not complete.
    /// </summary>
    /// <param name="candidate">The model candidate whose reservation should be released.</param>
    /// <param name="reservedTokens">The number of tokens that were originally reserved.</param>
    /// <param name="tenantId">The tenant whose budget is released.</param>
    void ReleaseReservation(ModelCandidate candidate, int reservedTokens, Guid tenantId);

    /// <summary>
    /// Returns the total amount the given tenant has spent today on platform models.
    /// </summary>
    /// <param name="tenantId">The tenant whose spend is queried.</param>
    /// <returns>A <see cref="Money"/> value representing today's cumulative spend for the tenant.</returns>
    Money GetTodaySpent(Guid tenantId);

    /// <summary>
    /// Records a single platform-provided search call for the tenant and returns whether the tenant is still
    /// within its daily search quota. Returns <c>false</c> (and does not record) when the quota is exhausted.
    /// BYO-SerpApi (tenant-owned) search must bypass this entirely.
    /// </summary>
    /// <param name="tenantId">The tenant making the platform search call.</param>
    bool TryRecordSearch(Guid tenantId);
}
