using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Credentials;

/// <summary>
/// Exposes the operator-configured platform model catalog (<c>RouterSettings.Candidates</c>) to all tenants.
/// These are the "platform-*" models used when a tenant has no BYO model key.
/// </summary>
internal sealed class PlatformModelsProvider : IPlatformModelProvider
{
    private readonly RouterSettings _routerSettings;

    public PlatformModelsProvider(IOptions<RouterSettings> routerOptions)
    {
        _routerSettings = routerOptions.Value;
    }

    public IReadOnlyList<ModelCandidate> GetCandidates() =>
        _routerSettings.Candidates
            .Select(c => new ModelCandidate(c.ModelId, c.Provider, c.Priority))
            .ToList();
}
