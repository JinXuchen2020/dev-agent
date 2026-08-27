using AgentPlatform.Application.Routing.Services;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Exposes the platform model catalog (DB-backed <c>PlatformModels</c> table) for tenants
/// without a BYO model key. These are the "platform-*" models available to every tenant.
/// When the table is empty it falls back to the <c>OpenAI:*</c> configuration.
/// </summary>
public interface IPlatformModelProvider
{
    /// <summary>Returns the list of platform model candidates available to all tenants.</summary>
    IReadOnlyList<ModelCandidate> GetCandidates();
}
