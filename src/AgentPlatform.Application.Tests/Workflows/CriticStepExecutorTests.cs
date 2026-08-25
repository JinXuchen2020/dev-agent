using System.Collections.Generic;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Infrastructure.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AgentPlatform.Application.Tests.Workflows;

/// <summary>
/// F31 ②：Critic 节点的模型调用经 IModelRouter（租户 BYO → 平台回退），不再硬编码
/// DefaultModelId 直连平台客户端；AllowCriticOverride 的 fail-loud / fail-open 语义保持不变。
/// </summary>
public sealed class CriticStepExecutorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly IModelRouter _modelRouter = Substitute.For<IModelRouter>();
    private readonly ILogger<CriticStepExecutor> _logger = Substitute.For<ILogger<CriticStepExecutor>>();

    private CriticStepExecutor CreateSut(bool allowOverride = false)
        => new(_logger, _modelRouter, Options.Create(new StateMachineSettings { AllowCriticOverride = allowOverride }));

    private static WorkflowContext CreateContextWithArtifact()
        => new()
        {
            WorkflowId = Guid.NewGuid(),
            CurrentStepOrder = 1,
            Artifacts = new Dictionary<string, StepArtifact>
            {
                ["Architect"] = new StepArtifact
                {
                    StepName = "Architect",
                    StepOrder = 0,
                    Content = "{\"design\":\"四层架构\"}",
                    ContentType = "architecture"
                }
            },
            Blackboard = Blackboard.Empty,
            Retrieval = RetrievalContext.Empty,
            Summary = StepHistory.Empty,
            TenantId = TenantId
        };

    private static IWorkflowExecutable CreateNode()
        => new WorkflowNode(Guid.NewGuid(), StepType.Critic, "Critic", 0, 0, "{}", null);

    [Fact]
    public async Task RoutesThroughModelRouter_WithTenantId_NoPreferredModel()
    {
        RoutingRequest? captured = null;
        _modelRouter.RouteAsync(Arg.Do<RoutingRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse("{\"approved\":true,\"stepName\":\"Architect\"}", null, "deepseek-chat", "stop"));
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(CreateNode(), CreateContextWithArtifact(), CancellationToken.None);

        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.NotNull(captured);
        Assert.Equal(TenantId, captured!.TenantId);
        Assert.Null(captured.PreferredModel); // Critic 无 agent 绑定，不指定偏好模型
        await _modelRouter.Received(1).RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ModelError_OverrideDisabled_RejectsFailLoud()
    {
        _modelRouter.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Throws(new ModelNotConfiguredException(TenantId));
        var sut = CreateSut(allowOverride: false);

        var result = await sut.ExecuteAsync(CreateNode(), CreateContextWithArtifact(), CancellationToken.None);

        // 回归保护：fail-loud 语义不变 —— 模型不可用且未开 override 时必须拒绝
        Assert.Equal(StepOutcome.Success, result.Outcome); // critic 自身成功产出"拒绝"评审结果
        Assert.Contains("\"Approved\":false", result.Output);
        Assert.Contains("critic model error", result.Output);
    }

    [Fact]
    public async Task ModelError_OverrideEnabled_AutoApproves()
    {
        _modelRouter.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Throws(new ModelNotConfiguredException(TenantId));
        var sut = CreateSut(allowOverride: true);

        var result = await sut.ExecuteAsync(CreateNode(), CreateContextWithArtifact(), CancellationToken.None);

        // 回归保护：AllowCriticOverride=true 时自动批准语义不变
        Assert.Contains("\"Approved\":true", result.Output);
        Assert.Contains("Auto-approved", result.Output);
    }

    [Fact]
    public async Task NoArtifacts_ReturnsSuccessWithoutRouting()
    {
        var context = new WorkflowContext
        {
            WorkflowId = Guid.NewGuid(),
            CurrentStepOrder = 0,
            Artifacts = new Dictionary<string, StepArtifact>(),
            Blackboard = Blackboard.Empty,
            Retrieval = RetrievalContext.Empty,
            Summary = StepHistory.Empty,
            TenantId = TenantId
        };
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(CreateNode(), context, CancellationToken.None);

        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.Contains("No artifacts", result.Output);
        await _modelRouter.DidNotReceive().RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>());
    }
}