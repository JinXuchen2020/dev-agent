using AgentPlatform.Application.Routing.Services;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Exposes the operator-configured platform model catalog (<c>RouterSettings.Candidates</c>) for tenants
/// without a BYO model key. These are the "platform-*" models available to every tenant.
/// </summary>
public interface IPlatformModelProvider
{
    /// <summary>Returns the list of platform model candidates available to all tenants.</summary>
    IReadOnlyList<ModelCandidate> GetCandidates();
}
