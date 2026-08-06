using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Debug.Queries.GetDebugVariables;

/// <summary>Returns the accumulated blackboard variables captured in a debug session.</summary>
public sealed record GetDebugVariablesQuery(Guid SessionId, Guid TenantId)
    : IRequest<DebugDtos.DebugVariablesResponse>;

internal sealed class GetDebugVariablesQueryHandler(IDebugSessionRepository sessionRepo)
    : IRequestHandler<GetDebugVariablesQuery, DebugDtos.DebugVariablesResponse>
{
    public async Task<DebugDtos.DebugVariablesResponse> Handle(GetDebugVariablesQuery request, CancellationToken ct)
    {
        var session = await sessionRepo.GetByIdAsync(request.SessionId, ct)
            ?? throw new KeyNotFoundException($"Debug session '{request.SessionId}' was not found.");
        return new DebugDtos.DebugVariablesResponse(session.GetVariables());
    }
}
