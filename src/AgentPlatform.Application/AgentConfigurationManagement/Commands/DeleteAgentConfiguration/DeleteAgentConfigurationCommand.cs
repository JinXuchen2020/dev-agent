using AgentPlatform.Application.Abstractions;
using MediatR;

namespace AgentPlatform.Application.AgentConfigurationManagement.Commands.DeleteAgentConfiguration;

/// <summary>
/// Command to delete an agent configuration by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the configuration to delete.</param>
public sealed record DeleteAgentConfigurationCommand(Guid Id) : ICommand<bool>;

internal sealed class DeleteAgentConfigurationCommandHandler(
    Domain.Repositories.IAgentConfigurationRepository repository)
    : IRequestHandler<DeleteAgentConfigurationCommand, bool>
{
    public async Task<bool> Handle(
        DeleteAgentConfigurationCommand request, CancellationToken ct)
    {
        var config = await repository.GetByIdAsync(request.Id, ct);
        if (config == null)
            return false;

        repository.Remove(config);
        return true;
    }
}
