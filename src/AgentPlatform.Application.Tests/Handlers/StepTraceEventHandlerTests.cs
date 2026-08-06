#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.EventHandlers;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Handlers;

/// <summary>
/// Verifies the F24 execution-trace pipeline: token usage and node type raised on the
/// <see cref="StepCompleted"/> / <see cref="StepFailed"/> domain events must be persisted
/// onto the <see cref="ExecutionLogEntry"/> written by the corresponding event handlers.
/// </summary>
public class StepTraceEventHandlerTests
{
    // ----- helpers -----------------------------------------------------------

    private static ExecutionLog NewLog() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "wf", Guid.NewGuid(), 1);

    private static IExecutionLogRepository RepoReturning(ExecutionLog log) =>
        RepoReturning(new List<ExecutionLog> { log });

    private static IExecutionLogRepository RepoReturning(IReadOnlyList<ExecutionLog> logs)
    {
        var repo = Substitute.For<IExecutionLogRepository>();
        repo.GetByWorkflowIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ExecutionLog>)logs);
        return repo;
    }

    private static (IUnitOfWork, IExecutionProgressBroadcaster, ILogger<T>) Mocks<T>()
    {
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        var broadcaster = Substitute.For<IExecutionProgressBroadcaster>();
        broadcaster.PublishAsync(Arg.Any<Guid>(), Arg.Any<ExecutionProgressEvent>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        var logger = Substitute.For<ILogger<T>>();
        return (uow, broadcaster, logger);
    }

    // ----- StepCompleted -----------------------------------------------------

    [Fact]
    public async Task StepCompleted_Persists_Tokens_And_NodeType_WhenProvided()
    {
        var log = NewLog();
        var repo = RepoReturning(log);
        var (uow, broadcaster, logger) = Mocks<StepCompletedEventHandler>();
        var handler = new StepCompletedEventHandler(repo, uow, broadcaster, logger);

        var usage = new TokenUsage(120, 30);
        var evt = new StepCompleted(log.WorkflowId, Guid.NewGuid(), "Agent Step", 0,
            "result", TimeSpan.FromSeconds(2), StepType.Agent, usage);

        await handler.Handle(new DomainEventNotification<StepCompleted>(evt), CancellationToken.None);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(120, entry.TokensIn);
        Assert.Equal(30, entry.TokensOut);
        Assert.Equal(StepType.Agent, entry.NodeType);
        Assert.Equal(WorkflowState.Completed, entry.Status);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StepCompleted_Defaults_Tokens_To_Zero_WhenUsageNull()
    {
        var log = NewLog();
        var repo = RepoReturning(log);
        var (uow, broadcaster, logger) = Mocks<StepCompletedEventHandler>();
        var handler = new StepCompletedEventHandler(repo, uow, broadcaster, logger);

        var evt = new StepCompleted(log.WorkflowId, Guid.NewGuid(), "Legacy Step", 0,
            "result", TimeSpan.FromSeconds(1), null, null);

        await handler.Handle(new DomainEventNotification<StepCompleted>(evt), CancellationToken.None);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(0, entry.TokensIn);
        Assert.Equal(0, entry.TokensOut);
        Assert.Null(entry.NodeType);
    }

    [Fact]
    public async Task StepCompleted_NoOp_WhenLogNotFound()
    {
        var repo = RepoReturning(Array.Empty<ExecutionLog>());
        var (uow, broadcaster, logger) = Mocks<StepCompletedEventHandler>();
        var handler = new StepCompletedEventHandler(repo, uow, broadcaster, logger);

        var evt = new StepCompleted(Guid.NewGuid(), Guid.NewGuid(), "x", 0, "r",
            TimeSpan.Zero, null, new TokenUsage(1, 1));

        await handler.Handle(new DomainEventNotification<StepCompleted>(evt), CancellationToken.None);

        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ----- StepFailed --------------------------------------------------------

    [Fact]
    public async Task StepFailed_Persists_Tokens_And_NodeType_WhenProvided()
    {
        var log = NewLog();
        var repo = RepoReturning(log);
        var (uow, broadcaster, logger) = Mocks<StepFailedEventHandler>();
        var handler = new StepFailedEventHandler(repo, uow, broadcaster, logger);

        var usage = new TokenUsage(50, 10);
        var evt = new StepFailed(log.WorkflowId, Guid.NewGuid(), "Tool Step", 1,
            "boom", TimeSpan.FromSeconds(1), StepType.Tool, usage);

        await handler.Handle(new DomainEventNotification<StepFailed>(evt), CancellationToken.None);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(50, entry.TokensIn);
        Assert.Equal(10, entry.TokensOut);
        Assert.Equal(StepType.Tool, entry.NodeType);
        Assert.Equal(WorkflowState.Failed, entry.Status);
        Assert.Equal("boom", entry.ErrorDetail);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StepFailed_Defaults_Tokens_To_Zero_WhenUsageNull()
    {
        var log = NewLog();
        var repo = RepoReturning(log);
        var (uow, broadcaster, logger) = Mocks<StepFailedEventHandler>();
        var handler = new StepFailedEventHandler(repo, uow, broadcaster, logger);

        var evt = new StepFailed(log.WorkflowId, Guid.NewGuid(), "Legacy Step", 1,
            "err", TimeSpan.FromSeconds(1), null, null);

        await handler.Handle(new DomainEventNotification<StepFailed>(evt), CancellationToken.None);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(0, entry.TokensIn);
        Assert.Equal(0, entry.TokensOut);
        Assert.Null(entry.NodeType);
    }

    // ----- StepExecutionResult token threading -------------------------------

    [Fact]
    public void StepExecutionResult_Success_Carries_TokenUsage_Through_Tokens()
    {
        var usage = new TokenUsage(7, 3);
        var result = StepExecutionResult.Success("out", "art", TimeSpan.FromSeconds(1), tokenUsage: usage);

        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.Equal(usage, result.Tokens);
        Assert.Equal(7, result.Tokens!.PromptTokens);
        Assert.Equal(3, result.Tokens.CompletionTokens);
    }

    [Fact]
    public void StepExecutionResult_Factory_Defaults_TokenUsage_Null()
    {
        var ok = StepExecutionResult.Success("out");
        var retry = StepExecutionResult.RetryableFailure("e");
        var fatal = StepExecutionResult.FatalFailure("e");
        var human = StepExecutionResult.NeedsIntervention("e");

        Assert.Null(ok.Tokens);
        Assert.Null(retry.Tokens);
        Assert.Null(fatal.Tokens);
        Assert.Null(human.Tokens);
    }
}
