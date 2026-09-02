using System.Diagnostics.Metrics;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Diagnostics;
using AgentPlatform.Application.Evaluation;
using AgentPlatform.Application.Evaluation.Commands.RunEvaluation;
using AgentPlatform.Application.Evaluation.Commands.RunEvaluationGate;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Evaluation;

/// <summary>
/// F39 观测埋点验证：门禁判定必须真实产出 <c>evaluation.gate.total{passed}</c> 计数
/// （MeterListener 断言测量事件本身，而非「方法存在即正确」）。
/// 阻断率告警与面板完全依赖该序列存在。
/// </summary>
public sealed class EvaluationGateMetricsTests
{
    private static void SetupGateReport(IMediator mediator, double score, int total) =>
        mediator.Send(Arg.Any<RunEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(new EvaluationReport(total, (int)Math.Round(score * total), score, []));

    private static RunEvaluationGateCommandHandler BuildHandler(IMediator mediator)
    {
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.GetTenantId().Returns(Guid.NewGuid());
        return new RunEvaluationGateCommandHandler(
            mediator,
            Substitute.For<IAuditLogRepository>(),
            tenantProvider,
            Options.Create(new EvaluationSettings { GateMinPassRate = 0.8 }),
            Substitute.For<ILogger<RunEvaluationGateCommandHandler>>());
    }

    [Fact]
    public async Task Gate_Emits_Counter_Tagged_Passed_For_Both_Verdicts()
    {
        var seen = new List<(int Count, string? Passed)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter == WorkflowMetrics.Meter && instrument.Name == "evaluation.gate.total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, state) =>
        {
            string? passed = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "passed")
                {
                    passed = tag.Value as string;
                }
            }

            seen.Add((value, passed));
        });
        listener.Start();

        var passingMediator = Substitute.For<IMediator>();
        SetupGateReport(passingMediator, score: 0.95, total: 4);
        await BuildHandler(passingMediator).Handle(
            new RunEvaluationGateCommand(Guid.NewGuid(), Guid.NewGuid(), null), CancellationToken.None);

        var blockingMediator = Substitute.For<IMediator>();
        SetupGateReport(blockingMediator, score: 0.25, total: 4);
        await BuildHandler(blockingMediator).Handle(
            new RunEvaluationGateCommand(Guid.NewGuid(), Guid.NewGuid(), null), CancellationToken.None);

        Assert.Contains(seen, m => m.Count == 1 && m.Passed == "true");
        Assert.Contains(seen, m => m.Count == 1 && m.Passed == "false");
    }
}
