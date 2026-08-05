using AgentPlatform.Application.Evaluation;
using AgentPlatform.Application.Evaluation.Commands.CreateEvaluationDataset;
using AgentPlatform.Application.Evaluation.Commands.DeleteEvaluationDataset;
using AgentPlatform.Application.Evaluation.Commands.RunEvaluation;
using AgentPlatform.Application.Evaluation.Commands.UpdateEvaluationDataset;
using AgentPlatform.Application.Evaluation.Queries.GetEvaluationDataset;
using AgentPlatform.Application.Evaluation.Queries.ListEvaluationDatasets;
using AgentPlatform.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// API controller for managing evaluation datasets and running dataset regression
/// evaluations against workflows. Routes are prefixed with <c>api/v1/evaluation-datasets</c>.
/// Reads are available to any authenticated caller; writes require Admin or Operator.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/evaluation-datasets")]
public sealed class EvaluationDatasetsController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="EvaluationDatasetsController"/> class.
    /// </summary>
    public EvaluationDatasetsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Lists the caller's tenant datasets, optionally filtered by keyword.</summary>
    [HttpGet]
    public async Task<IActionResult> ListDatasets([FromQuery] string? keyword, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListEvaluationDatasetsQuery(keyword), ct);
        return Ok(result);
    }

    /// <summary>Gets a single dataset including its cases.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDataset(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetEvaluationDatasetQuery(id), ct);
        return Ok(result);
    }

    /// <summary>Creates a new evaluation dataset.</summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost]
    public async Task<IActionResult> CreateDataset(
        [FromBody] CreateEvaluationDatasetRequest request, CancellationToken ct)
    {
        var command = new CreateEvaluationDatasetCommand(
            request.Name,
            request.Description,
            request.Cases.Select(Map).ToList());
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>Replaces a dataset's name, description, and cases.</summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateDataset(
        Guid id, [FromBody] UpdateEvaluationDatasetRequest request, CancellationToken ct)
    {
        var command = new UpdateEvaluationDatasetCommand(
            id,
            request.Name,
            request.Description,
            request.Cases.Select(Map).ToList());
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>Deletes a dataset.</summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDataset(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteEvaluationDatasetCommand(id), ct);
        return NoContent();
    }

    /// <summary>Runs a dataset regression evaluation against a target workflow.</summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/run")]
    public async Task<IActionResult> RunDataset(
        Guid id, [FromBody] RunEvaluationRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new RunEvaluationCommand(id, request.WorkflowId), ct);
        return Ok(result);
    }

    private static CreateEvaluationCaseDto Map(CreateEvaluationCaseRequest r) =>
        new(r.Input, r.ExpectedOutput, r.MatchMode);
}

/// <summary>API request body for creating an evaluation dataset.</summary>
public sealed record CreateEvaluationDatasetRequest(
    string Name,
    string? Description,
    List<CreateEvaluationCaseRequest> Cases);

/// <summary>API request body for a single evaluation case.</summary>
public sealed record CreateEvaluationCaseRequest(
    string Input,
    string ExpectedOutput,
    EvaluationMatchMode MatchMode);

/// <summary>API request body for updating an evaluation dataset.</summary>
public sealed record UpdateEvaluationDatasetRequest(
    string Name,
    string? Description,
    List<CreateEvaluationCaseRequest> Cases);

/// <summary>API request body for running an evaluation against a workflow.</summary>
public sealed record RunEvaluationRequest(Guid WorkflowId);
