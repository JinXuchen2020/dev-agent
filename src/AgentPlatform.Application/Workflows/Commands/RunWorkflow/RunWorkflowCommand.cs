using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using System.ComponentModel.DataAnnotations;

namespace AgentPlatform.Application.Workflows.Commands.RunWorkflow;

/// <summary>
/// Represents a command to create and start a new workflow with the specified name and initial context.
/// </summary>
/// <param name="Name">The display name of the workflow.</param>
/// <param name="InitialContext">The initial context data to seed the workflow with.</param>
/// <param name="TenantId">The unique identifier of the tenant that owns the workflow.</param>
public record RunWorkflowCommand(
    [Required] string Name,
    [Required] string InitialContext,
    Guid TenantId
) : ICommand<Workflow>;
