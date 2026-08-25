using System.Collections.Generic;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.ValueObjects;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Workflows;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AgentPlatform.Application.Tests.Workflows;

/// <summary>
/// F31 验收：AgentCallStepExecutor 运行时实体化 —— 绑定 agent 时其 SystemPrompt 与
/// ModelEndpoint.ModelName（PreferredModel）真实生效；未绑定节点向后兼容走通用模板；
/// agent 缺失时 fail-loud 而非静默回退。
/// </summary>
public sealed class AgentCallStepExecutorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly IAgentRepository _agentRepository = Substitute.For<IAgentRepository>();
    private readonly IModelRouter _modelRouter = Substitute.For<IModelRouter>();
    private readonly ILogger<AgentCallStepExecutor> _logger = Substitute.For<ILogger<AgentCallStepExecutor>>();

    private AgentCallStepExecutor CreateSut() => new(_logger, _agentRepository, _modelRouter);

    private static WorkflowContext CreateContext()
        => new()
        {
            WorkflowId = Guid.NewGuid(),
            CurrentStepOrder = 0,
            Artifacts = new Dictionary<string, StepArtifact>(),
            Blackboard = Blackboard.Empty,
            Retrieval = RetrievalContext.Empty,
            Summary = StepHistory.Empty,
            TenantId = TenantId
        };

    private static IWorkflowExecutable CreateNode(Guid? agentId)
        => new WorkflowNode(Guid.NewGuid(), StepType.LLM, "Architect", 0, 0, "{}", agentId);

    private static Agent CreateAgent(string systemPrompt = "你是资深系统架构师，负责产出架构设计。")
        => new(
            Guid.NewGuid(),
            "架构测试智能体",
            AgentType.Architecture,
            new ModelEndpoint("DeepSeek", "deepseek-chat", "https://api.deepseek.com/v1"),
            systemPrompt,
            TenantId);

    private RoutingRequest? _capturedRequest;

    private void SetupRouterResponse(string content = "架构输出")
    {
        _modelRouter.RouteAsync(Arg.Do<RoutingRequest>(r => _capturedRequest = r), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse(content, null, "deepseek-chat", "stop"));
    }

    [Fact]
    public async Task BoundAgent_UsesSystemPrompt_AndPreferredModel()
    {
        var agent = CreateAgent();
        _agentRepository.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        SetupRouterResponse();
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(CreateNode(agent.Id), CreateContext(), CancellationToken.None);

        Assert.Equal(StepOutcome.Success, result.Outcome);
        await _modelRouter.Received(1).RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>());
        Assert.NotNull(_capturedRequest);
        Assert.Equal(TenantId, _capturedRequest!.TenantId);
        // PreferredModel 必须来自 agent 的 ModelEndpoint，而非 StateMachineSettings.DefaultModelId
        Assert.Equal("deepseek-chat", _capturedRequest.PreferredModel);
        // System prompt 必须是 agent 配置的真实值，而非通用模板
        Assert.Equal("你是资深系统架构师，负责产出架构设计。", _capturedRequest.Messages[0].Content);
        Assert.Contains("Architect", _capturedRequest.Messages[1].Content);
    }

    [Fact]
    public async Task UnboundNode_KeepsLegacyPrompt_RoutesWithoutPreference()
    {
        SetupRouterResponse();
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(CreateNode(null), CreateContext(), CancellationToken.None);

        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.NotNull(_capturedRequest);
        // 向后兼容（验收 #5）：未绑定节点不携带偏好模型，prompt 为既有通用模板
        Assert.Null(_capturedRequest!.PreferredModel);
        Assert.Contains("executing the step \"Architect\"", _capturedRequest.Messages[0].Content);
        await _agentRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundAgentMissing_FailsLoud_WithClearError()
    {
        var missingId = Guid.NewGuid();
        _agentRepository.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((Agent?)null);
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(CreateNode(missingId), CreateContext(), CancellationToken.None);

        // fail-loud（D3）：不静默回退默认模型，否则「配而不生效」以新形态复发。
        // 走 FailedRetry 进入既有重试管线，重试耗尽后由编排器回滚并落 ErrorDetail。
        Assert.Equal(StepOutcome.FailedRetry, result.Outcome);
        Assert.Contains(missingId.ToString(), result.ErrorMessage);
        Assert.Contains("不存在", result.ErrorMessage);
        await _modelRouter.DidNotReceive().RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouterFailure_ReturnsRetryableFailure_WithOriginalMessage()
    {
        var agent = CreateAgent();
        _agentRepository.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        _modelRouter.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Throws(new ModelNotConfiguredException(TenantId));
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(CreateNode(agent.Id), CreateContext(), CancellationToken.None);

        Assert.Equal(StepOutcome.FailedRetry, result.Outcome);
        Assert.Contains("未配置任何可用模型", result.ErrorMessage);
    }

    [Fact]
    public async Task Success_PropagatesOutput_AndAgentNameInArtifact()
    {
        var agent = CreateAgent();
        _agentRepository.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        SetupRouterResponse("四层架构设计……");
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(CreateNode(agent.Id), CreateContext(), CancellationToken.None);

        Assert.Equal("四层架构设计……", result.Output);
        Assert.NotNull(result.Artifact);
        // System.Text.Json 默认转义非 ASCII，故断言结构键而非中文名原文
        Assert.Contains("\"step\":\"Architect\"", result.Artifact);
        Assert.Contains("\"agent\":", result.Artifact);
    }

    [Fact]
    public async Task Prompt_Renders_SemanticRecall_And_Retrieval_Sections()
    {
        // F33：Summary（含 [semantic-recall] 召回条目）与 Retrieval 片段必须真正进入 prompt
        string? capturedPrompt = null;
        var agent = CreateAgent();
        _agentRepository.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        _modelRouter.RouteAsync(Arg.Do<RoutingRequest>(r => capturedPrompt = r.Messages[1].Content),
                Arg.Any<CancellationToken>())
            .Returns(new ModelResponse("ok", null, "deepseek-chat", "stop"));

        var ctx = CreateContext();
        var summaries = new Dictionary<int, string>
        {
            [0] = "[0] step-0: 早期产出",
            [-1] = "[semantic-recall] 历史相似运行的经验教训"
        };
        ctx = ctx with
        {
            Summary = new StepHistory { Summaries = summaries, MaxTokens = 8000, EstimatedTokenCount = 50 },
            Retrieval = new RetrievalContext
            {
                Chunks = new List<string> { "知识库召回片段内容" },
                Sources = new List<string> { "doc-1" }
            }
        };

        var sut = CreateSut();
        await sut.ExecuteAsync(CreateNode(agent.Id), ctx, CancellationToken.None);

        Assert.NotNull(capturedPrompt);
        Assert.Contains("History summary:", capturedPrompt);
        Assert.Contains("[semantic-recall]", capturedPrompt);
        Assert.Contains("历史相似运行的经验教训", capturedPrompt);
        Assert.Contains("Relevant knowledge:", capturedPrompt);
        Assert.Contains("知识库召回片段内容", capturedPrompt);
    }
}