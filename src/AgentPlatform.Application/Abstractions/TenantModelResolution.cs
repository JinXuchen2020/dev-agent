using AgentPlatform.Application.Routing.Services;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Result of resolving a tenant's model client: the client to use plus the candidate model list it registered.
/// </summary>
/// <param name="Client">The per-tenant <see cref="IModelClient"/> built from the tenant's BYO credential.</param>
/// <param name="Candidates">The candidate models registered on that client.</param>
public sealed record TenantModelResolution(IModelClient Client, IReadOnlyList<ModelCandidate> Candidates);
