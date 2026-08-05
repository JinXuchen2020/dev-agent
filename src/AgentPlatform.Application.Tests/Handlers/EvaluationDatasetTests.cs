#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Evaluation;
using AgentPlatform.Application.Evaluation.Commands.CreateEvaluationDataset;
using AgentPlatform.Application.Evaluation.Commands.RunEvaluation;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Evaluation;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Handlers;

public class EvaluationDatasetTests
{
    [Fact]
    public void EvaluationDataset_Update_Replaces_Cases()
    {
        var ds = new EvaluationDataset(Guid.NewGuid(), Guid.NewGuid(), "orig");
        ds.Update("orig", null, new List<EvaluationCase>
        {
            new(Guid.NewGuid(), "in1", "out1", EvaluationMatchMode.Exact)
        });
        Assert.Single(ds.Cases);

        ds.Update("renamed", "desc", new List<EvaluationCase>
        {
            new(Guid.NewGuid(), "inA", "outA", EvaluationMatchMode.Contains),
            new(Guid.NewGuid(), "inB", "outB", EvaluationMatchMode.Exact)
        });

        Assert.Equal(2, ds.Cases.Count);
        Assert.Equal("renamed", ds.Name);
        Assert.Equal("desc", ds.Description);
        Assert.Contains(ds.Cases, c => c.Input == "inA" && c.MatchMode == EvaluationMatchMode.Contains);
        Assert.DoesNotContain(ds.Cases, c => c.Input == "in1");
    }

    // ----- Create handler ----------------------------------------------------

    [Fact]
    public async Task CreateEvaluationDataset_Returns_Mapped_Detail()
    {
        var repo = Substitute.For<IEvaluationDatasetRepository>();
        var tenant = Substitute.For<ITenantProvider>();
        tenant.GetTenantId().Returns(Guid.NewGuid());
        var settings = Options.Create(new EvaluationSettings { MaxCases = 10 });

        var handler = new CreateEvaluationDatasetCommandHandler(repo, tenant, settings);
        var cmd = new CreateEvaluationDatasetCommand("DS", "desc",
            new List<CreateEvaluationCaseDto>
            {
                new("in1", "out1", EvaluationMatchMode.Exact)
            });

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.Equal("DS", result.Name);
        Assert.Equal("desc", result.Description);
        Assert.Single(result.Cases);
        Assert.Equal("in1", result.Cases[0].Input);
        repo.Received(1).Add(Arg.Any<EvaluationDataset>());
    }

    [Fact]
    public async Task CreateEvaluationDataset_Rejects_When_Exceeds_MaxCases()
    {
        var repo = Substitute.For<IEvaluationDatasetRepository>();
        var tenant = Substitute.For<ITenantProvider>();
        tenant.GetTenantId().Returns(Guid.NewGuid());
        var settings = Options.Create(new EvaluationSettings { MaxCases = 2 });

        var handler = new CreateEvaluationDatasetCommandHandler(repo, tenant, settings);
        var cmd = new CreateEvaluationDatasetCommand("DS", null, new List<CreateEvaluationCaseDto>
        {
            new("a", "b", EvaluationMatchMode.Exact),
            new("c", "d", EvaluationMatchMode.Exact),
            new("e", "f", EvaluationMatchMode.Exact)
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(cmd, CancellationToken.None));
    }

    // ----- RunEvaluation handler --------------------------------------------

    [Fact]
    public async Task RunEvaluation_Contains_Match_Passes_And_Sums_Tokens()
    {
        var tenantId = Guid.NewGuid();
        var (handler, cmd) = BuildRunHandler(tenantId,
            expected: "output", mode: EvaluationMatchMode.Contains,
            actual: "this is the actual output",
            tokensIn: 5, tokensOut: 3);

        var report = await handler.Handle(cmd, CancellationToken.None);

        Assert.Equal(1, report.Total);
        Assert.Equal(1, report.Passed);
        Assert.Equal(1.0, report.Score);
        Assert.True(report.Cases[0].Passed);
        Assert.Equal(5, report.Cases[0].TokensIn);
        Assert.Equal(3, report.Cases[0].TokensOut);
    }

    [Fact]
    public async Task RunEvaluation_Exact_Mismatch_Fails()
    {
        var tenantId = Guid.NewGuid();
        var (handler, cmd) = BuildRunHandler(tenantId,
            expected: "exact text", mode: EvaluationMatchMode.Exact,
            actual: "different text",
            tokensIn: 0, tokensOut: 0);

        var report = await handler.Handle(cmd, CancellationToken.None);

        Assert.Equal(1, report.Total);
        Assert.Equal(0, report.Passed);
        Assert.Equal(0.0, report.Score);
        Assert.False(report.Cases[0].Passed);
    }

    private static (RunEvaluationCommandHandler, RunEvaluationCommand) BuildRunHandler(
        Guid tenantId, string expected, EvaluationMatchMode mode, string actual, int tokensIn, int tokensOut)
    {
        var dataset = new EvaluationDataset(Guid.NewGuid(), tenantId, "DS");
        dataset.Update("DS", null, new List<EvaluationCase>
        {
            new(Guid.NewGuid(), "input", expected, mode)
        });

        var source = new Workflow(Guid.NewGuid(), "src", tenantId);
        source.AddStep(new WorkflowStep(Guid.NewGuid(), 0, "step1"));

        var primitive = Substitute.For<IOrchestrationPrimitive>();
        var runResult = new Workflow(Guid.NewGuid(), "ran", tenantId);
        var outStep = new WorkflowStep(Guid.NewGuid(), 0, "step1");
        outStep.SetResult(actual);
        outStep.SetState(WorkflowState.Completed);
        runResult.AddStep(outStep);
        primitive.RunAsync(Arg.Any<Workflow>(), Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(runResult));

        var datasetRepo = Substitute.For<IEvaluationDatasetRepository>();
        datasetRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(dataset);

        var workflowRepo = Substitute.For<IWorkflowRepository>();
        workflowRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(source);

        var execLogRepo = Substitute.For<IExecutionLogRepository>();
        var log = new ExecutionLog(Guid.NewGuid(), runResult.Id, "ran", tenantId, 1);
        log.AddEntry(new ExecutionLogEntry(Guid.NewGuid(), "step1", 0, WorkflowState.Completed,
            duration: TimeSpan.Zero, result: actual, errorDetail: null,
            tokensIn: tokensIn, tokensOut: tokensOut, nodeType: null));
        execLogRepo.GetByWorkflowIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ExecutionLog>)new List<ExecutionLog> { log });

        var auditRepo = Substitute.For<IAuditLogRepository>();
        var tenant = Substitute.For<ITenantProvider>();
        tenant.GetTenantId().Returns(tenantId);
        var settings = Options.Create(new EvaluationSettings { MaxCases = 10 });
        var logger = Substitute.For<ILogger<RunEvaluationCommandHandler>>();

        var handler = new RunEvaluationCommandHandler(
            datasetRepo, workflowRepo, execLogRepo, primitive, auditRepo, tenant, settings, logger);

        var cmd = new RunEvaluationCommand(dataset.Id, source.Id);
        return (handler, cmd);
    }
}
