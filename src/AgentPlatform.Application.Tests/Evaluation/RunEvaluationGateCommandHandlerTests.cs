using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Evaluation;
using AgentPlatform.Application.Evaluation.Commands.RunEvaluation;
using AgentPlatform.Application.Evaluation.Commands.RunEvaluationGate;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Evaluation;

/// <summary>
/// F34 验收①：在线评估门禁——阈值解析链（显式 > 配置）、空数据集不放行、
/// 阻断语义（Passed=false）与 EvaluationGate 审计。执行委托给 RunEvaluation
/// （影子克隆语义已在彼处覆盖，此处仅 mock 其返回报告）。
/// </summary>
public sealed class RunEvaluationGateCommandHandlerTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IAuditLogRepository _auditRepository = Substitute.For<IAuditLogRepository>();
    private readonly ITenantProvider _tenantProvider = Substitute.For<ITenantProvider>();
    private readonly List<AuditLog> _audits = [];

    private readonly Guid _datasetId = Guid.NewGuid();
    private readonly Guid _workflowId = Guid.NewGuid();

    private RunEvaluationGateCommandHandler CreateHandler(double configThreshold = 0.8)
    {
        _auditRepository.Add(Arg.Do<AuditLog>(a => _audits.Add(a)));
        _tenantProvider.GetTenantId().Returns(Guid.NewGuid());
        return new RunEvaluationGateCommandHandler(
            _mediator, _auditRepository, _tenantProvider,
            Options.Create(new EvaluationSettings { GateMinPassRate = configThreshold }),
            Substitute.For<ILogger<RunEvaluationGateCommandHandler>>());
    }

    private void SetupReport(double score, int total = 4)
    {
        _mediator.Send(Arg.Any<RunEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(new EvaluationReport(total, (int)Math.Round(score * total), score, []));
    }

    [Fact]
    public async Task Score_Above_Explicit_Threshold_Passes_And_Audits()
    {
        SetupReport(score: 0.9);

        var result = await CreateHandler(configThreshold: 0.8).Handle(
            new RunEvaluationGateCommand(_datasetId, _workflowId, MinPassRate: 0.7),
            CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal(0.7, result.MinPassRate); // 显式值覆盖配置默认
        Assert.Equal(4, result.Total);
        Assert.Single(_audits);
        Assert.Equal(AuditActionType.EvaluationGate, _audits[0].Action);
        await _mediator.Received(1).Send(Arg.Any<RunEvaluationCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Score_Below_ConfigThreshold_Blocks()
    {
        SetupReport(score: 0.5);

        var result = await CreateHandler(configThreshold: 0.8).Handle(
            new RunEvaluationGateCommand(_datasetId, _workflowId, MinPassRate: null),
            CancellationToken.None);

        // 阻断语义：CI / 发布流水线据此停止部署
        Assert.False(result.Passed);
        Assert.Equal(0.8, result.MinPassRate);
    }

    [Fact]
    public async Task Explicit_Threshold_Overrides_Config_Default()
    {
        SetupReport(score: 0.5);

        var result = await CreateHandler(configThreshold: 0.8).Handle(
            new RunEvaluationGateCommand(_datasetId, _workflowId, MinPassRate: 0.3),
            CancellationToken.None);

        // 显式放宽到 0.3：score 0.5 >= 0.3 → 通过（覆盖配置默认 0.8）
        Assert.True(result.Passed);
        Assert.Equal(0.3, result.MinPassRate);
    }

    [Fact]
    public async Task Empty_Dataset_Never_Passes_Even_With_Zero_Threshold()
    {
        SetupReport(score: 0, total: 0);

        var result = await CreateHandler().Handle(
            new RunEvaluationGateCommand(_datasetId, _workflowId, MinPassRate: 0),
            CancellationToken.None);

        // 防「无数据即放行」漏洞：Total=0 恒不通过（显式守卫，min=0 也拦）
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task Threshold_Out_Of_Range_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CreateHandler().Handle(
                new RunEvaluationGateCommand(_datasetId, _workflowId, MinPassRate: 1.5),
                CancellationToken.None));
    }
}