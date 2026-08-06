using System.Diagnostics;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Evaluation;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Evaluation;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Application.Evaluation.Commands.RunEvaluation;

/// <summary>
/// Runs a dataset regression evaluation against a target workflow. Each case is replayed
/// as the workflow's initial context; the last completed step's result is compared to the
/// expected output using the case's <see cref="EvaluationMatchMode"/>.
/// </summary>
public sealed record RunEvaluationCommand(Guid DatasetId, Guid WorkflowId) : ICommand<EvaluationReport>;

internal sealed class RunEvaluationCommandHandler(
    IEvaluationDatasetRepository datasetRepository,
    IWorkflowRepository workflowRepository,
    IExecutionLogRepository executionLogRepository,
    IOrchestrationPrimitive primitive,
    IAuditLogRepository auditLogRepository,
    ITenantProvider tenantProvider,
    IOptions<EvaluationSettings> evalSettings,
    ILogger<RunEvaluationCommandHandler> logger)
    : IRequestHandler<RunEvaluationCommand, EvaluationReport>
{
    public async Task<EvaluationReport> Handle(RunEvaluationCommand request, CancellationToken ct)
    {
        var dataset = await datasetRepository.GetByIdAsync(request.DatasetId, ct)
            ?? throw new KeyNotFoundException($"Evaluation dataset '{request.DatasetId}' was not found.");

        var source = await workflowRepository.GetByIdAsync(request.WorkflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow '{request.WorkflowId}' was not found.");

        var tenantId = tenantProvider.GetTenantId();
        var max = evalSettings.Value.MaxCases;
        var cases = dataset.Cases.Take(max).ToList();

        var results = new List<EvaluationCaseResult>();
        var passed = 0;

        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            var sw = Stopwatch.StartNew();
            string? actual = null;
            string? error = null;
            var tokensIn = 0;
            var tokensOut = 0;

            try
            {
                // Run a FRESH, throwaway clone of the source workflow. We must NOT mutate or
                // persist the caller's original workflow — the orchestrator calls
                // repository.Update(workflow)+SaveChanges, so a cloned instance (new id) keeps the
                // source entity's persisted state untouched.
                var evalWorkflow = new Workflow(Guid.NewGuid(), $"eval-{dataset.Id:N}-{i}", tenantId);
                foreach (var step in source.Steps)
                    evalWorkflow.AddStep(new WorkflowStep(Guid.NewGuid(), step.Order, step.StepName));
                evalWorkflow.UpdateContext(c.Input);

                var result = await primitive.RunAsync(evalWorkflow, OrchestrationPreset.Sequential, ct);

                var lastCompleted = result.Steps
                    .Where(s => s.State == WorkflowState.Completed && !string.IsNullOrEmpty(s.Result))
                    .OrderBy(s => s.Order)
                    .LastOrDefault();
                actual = lastCompleted?.Result;

                // Token accounting: the run produced an ExecutionLog (via WorkflowStarted /
                // StepCompleted domain events). Sum the per-step token usage for the report.
                var logs = await executionLogRepository.GetByWorkflowIdAsync(evalWorkflow.Id, ct);
                var log = logs.FirstOrDefault();
                if (log is not null)
                {
                    tokensIn = log.Entries.Sum(e => e.TokensIn);
                    tokensOut = log.Entries.Sum(e => e.TokensOut);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger.LogWarning(
                    ex, "Evaluation case {Index} for dataset {DatasetId} failed: {Error}", i, dataset.Id, ex.Message);
            }

            sw.Stop();

            var ok = error is null && actual is not null && Matches(c.MatchMode, actual, c.ExpectedOutput);
            if (ok) passed++;
            results.Add(new EvaluationCaseResult(
                c.Input, c.ExpectedOutput, actual, ok, sw.ElapsedMilliseconds, tokensIn, tokensOut, error));
        }

        var total = cases.Count;
        var score = total == 0 ? 0d : (double)passed / total;

        var audit = AuditLog.Record(
            tenantId,
            AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType.RunEvaluation,
            "EvaluationDataset",
            entityId: dataset.Id,
            details: $"Workflow {request.WorkflowId}: passed {passed}/{total}, score {score:P0}");
        auditLogRepository.Add(audit);

        return new EvaluationReport(total, passed, score, results);
    }

    private static bool Matches(EvaluationMatchMode mode, string actual, string expected) => mode switch
    {
        EvaluationMatchMode.Contains => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
        _ => string.Equals(actual, expected, StringComparison.Ordinal)
    };
}
