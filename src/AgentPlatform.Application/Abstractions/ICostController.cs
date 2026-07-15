using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.ValueObjects;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides operations for reserving, settling, and releasing estimated token costs against a daily budget.
/// </summary>
public interface ICostController
{
    /// <summary>
    /// Attempts to reserve an estimated token cost for the given candidate against the daily budget.
    /// </summary>
    /// <param name="candidate">The model candidate for which to reserve cost.</param>
    /// <param name="estimatedTokens">The estimated number of tokens for the upcoming request.</param>
    /// <returns><c>true</c> if the reservation was accepted; <c>false</c> if the budget would be exceeded.</returns>
    bool TryReserve(ModelCandidate candidate, int estimatedTokens);

    /// <summary>
    /// Reconciles a previously reserved cost against the actual token usage after the request completes.
    /// </summary>
    /// <param name="candidate">The model candidate whose usage is being settled.</param>
    /// <param name="actualTokenUsage">The actual token usage reported by the model, or <c>null</c> if unknown.</param>
    /// <param name="reservedTokens">The number of tokens that were originally reserved.</param>
    void SettleUsage(ModelCandidate candidate, TokenUsage? actualTokenUsage, int reservedTokens);

    /// <summary>
    /// Releases a previously reserved token cost back to the daily budget when the request did not complete.
    /// </summary>
    /// <param name="candidate">The model candidate whose reservation should be released.</param>
    /// <param name="reservedTokens">The number of tokens that were originally reserved.</param>
    void ReleaseReservation(ModelCandidate candidate, int reservedTokens);

    /// <summary>
    /// Returns the total amount spent today.
    /// </summary>
    /// <returns>A <see cref="Money"/> value representing today's cumulative spend.</returns>
    Money GetTodaySpent();
}
