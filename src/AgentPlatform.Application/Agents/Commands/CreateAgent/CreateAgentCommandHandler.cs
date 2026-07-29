using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Domain.ValueObjects;
using MediatR;

namespace AgentPlatform.Application.Agents.Commands.CreateAgent;

internal sealed class CreateAgentCommandHandler : IRequestHandler<CreateAgentCommand, Agent>
{
    private readonly IAgentRepository _repository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IAgentConfigurationRepository _configurationRepository;

    public CreateAgentCommandHandler(
        IAgentRepository repository,
        IAuditLogRepository auditLogRepository,
        IAgentConfigurationRepository configurationRepository)
    {
        _repository = repository;
        _auditLogRepository = auditLogRepository;
        _configurationRepository = configurationRepository;
    }

    public async Task<Agent> Handle(CreateAgentCommand request, CancellationToken ct)
    {
        var endpoint = new ModelEndpoint(
            request.ModelProvider,
            request.ModelName,
            request.ModelApiUrl);

        var role = AgentType.FromCode(request.RoleCode)
            ?? new AgentType(request.RoleCode, request.RoleCode, request.RoleCode);

        var agent = new Agent(
            Guid.NewGuid(),
            request.Name,
            role,
            endpoint,
            request.SystemPrompt,
            request.TenantId);

        _repository.Add(agent);

        var details = $"Created agent '{agent.Name}' with role {agent.Role.RoleCode}";

        // Best-effort provenance tracing (D1): record the source configuration version in the
        // audit log when one was supplied. Failures here must never block agent creation.
        if (request.ConfigurationId is { } configurationId)
        {
            try
            {
                var origin = await _configurationRepository.GetByIdAsync(configurationId, ct);
                if (origin != null && origin.TenantId == request.TenantId)
                    details += $" (from configuration '{origin.Name}' v{origin.Version})";
            }
            catch
            {
                // ignore tracing failures
            }
        }

        var auditLog = AuditLog.Record(
            tenantId: agent.TenantId,
            action: AuditActionType.CreateAgent,
            entity: "Agent",
            userId: null,
            entityId: agent.Id,
            details: details);
        _auditLogRepository.Add(auditLog);

        return agent;
    }
}
