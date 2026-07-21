namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Rotates API keys: generates fresh encrypted key material, advances the key
/// version on the aggregate, persists the change, and emits a rotation audit entry.
/// Intended for internal callers (e.g. the API-key expiry background job); it does
/// not expose any public HTTP endpoint.
/// </summary>
public interface IKeyRotationService
{
    /// <summary>
    /// Rotates the API key identified by <paramref name="apiKeyId"/>.
    /// If the key does not exist the call is a safe no-op.
    /// </summary>
    /// <param name="apiKeyId">The id of the key to rotate.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RotateKeyAsync(Guid apiKeyId, CancellationToken ct = default);

    /// <summary>
    /// Revokes the API key identified by <paramref name="apiKeyId"/>, deactivating it
    /// so it can no longer authenticate requests, and records a revocation audit entry.
    /// If the key does not exist the call is a safe no-op.
    /// </summary>
    /// <param name="apiKeyId">The id of the key to revoke.</param>
    /// <param name="ct">A cancellation token.</param>
    Task RevokeKeyAsync(Guid apiKeyId, CancellationToken ct = default);
}
