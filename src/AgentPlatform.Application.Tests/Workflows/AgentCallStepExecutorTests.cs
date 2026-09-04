using System.Collections.Generic;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.ValueObjects;
using AgentPlatform.Domain.Aggregates.Conversations;
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
    private readonly IConversationRepository _conversationRepository = Substitute.For<IConversationRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ILogger<AgentCallStepExecutor> _logger = Substitute.For<ILogger<AgentCallStepExecutor>>();

    private AgentCallStepExecutor CreateSut() =>
        new(_logger, _agentRepository, _modelRouter, _conversationRepository, _unitOfWork);

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

    // ── F36 · Agent 上下文隔离（D1 软分区视图 / D2 自动建会话 / D4 显式回写）──

    [Fact]
    public async Task AgentStep_Prompt_Uses_PartitionView_Other_Agent_Data_Invisible()
    {
        var agent = CreateAgent();
        var otherAgentId = Guid.NewGuid();
        _agentRepository.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        SetupRouterResponse();

        var ctx = CreateContext();
        ctx.Blackboard
            .Set("shared", "全局共享值")
            .SetInPartition(otherAgentId, "secret", "B 的中间产物")
            .SetInPartition(agent.Id, "plan", "A 自己的计划");

        var sut = CreateSut();
        await sut.ExecuteAsync(CreateNode(agent.Id), ctx, CancellationToken.None);

        Assert.NotNull(_capturedRequest);
        var prompt = _capturedRequest!.Messages[1].Content;
        Assert.Contains("shared: 全局共享值", prompt);
        Assert.Contains("plan: A 自己的计划", prompt); // 自分区键剥离前缀
        Assert.DoesNotContain("B 的中间产物", prompt); // 其他 agent 分区不可见
        Assert.DoesNotContain("secret", prompt);
    }

    [Fact]
    public async Task UnboundStep_Prompt_Uses_GlobalView_Excluding_Agent_Partitions()
    {
        var agentA = Guid.NewGuid();
        SetupRouterResponse();

        var ctx = CreateContext();
        ctx.Blackboard
            .Set("loop.x", "1")
            .SetInPartition(agentA, "plan", "A 的计划");

        var sut = CreateSut();
        await sut.ExecuteAsync(CreateNode(null), ctx, CancellationToken.None);

        Assert.NotNull(_capturedRequest);
        var prompt = _capturedRequest!.Messages[1].Content;
        Assert.Contains("loop.x: 1", prompt);
        Assert.DoesNotContain("A 的计划", prompt);
    }

    [Fact]
    public async Task AgentStep_Writes_OutputKey_To_Global_Blackboard()
    {
        var agent = CreateAgent();
        _agentRepository.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        SetupRouterResponse("最终架构产出");

        var ctx = CreateContext();
        var sut = CreateSut();
        var result = await sut.ExecuteAsync(CreateNode(agent.Id), ctx, CancellationToken.None);

        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.Equal("最终架构产出", ctx.Blackboard.Get(Blackboard.AgentOutputKey(agent.Id)));
    }

    [Fact]
    public async Task AgentStep_Creates_AgentConversation_With_Two_Messages()
    {
        var agent = CreateAgent();
        _agentRepository.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        SetupRouterResponse("agent 回复内容");
        _conversationRepository.GetByAgentAsync(TenantId, Arg.Any<Guid>(), agent.Id, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null); // 尚无会话 → 创建路径

        Conversation? added = null;
        // Arg.Do 回调须在调用发生前注册才能捕获实参
        _conversationRepository.Add(Arg.Do<Conversation>(c => added = c));

        var ctx = CreateContext();
        var sut = CreateSut();
        var result = await sut.ExecuteAsync(CreateNode(agent.Id), ctx, CancellationToken.None);

        Assert.Equal(StepOutcome.Success, result.Outcome);
        _conversationRepository.Received(1).Add(Arg.Any<Conversation>());
        Assert.NotNull(added);
        Assert.Equal(agent.Id, added!.AgentId);
        Assert.Equal(ctx.WorkflowId, added.WorkflowId);
        Assert.Equal(TenantId, added.TenantId);
        Assert.Equal(2, added.Messages.Count); // user（prompt 摘要）+ agent（回复）
        Assert.Contains("agent 回复内容", added.Messages[1].Content);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AgentStep_Reuses_Existing_AgentConversation()
    {
        var agent = CreateAgent();
        _agentRepository.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        SetupRouterResponse("第二次回复");
        var existing = new Conversation(Guid.NewGuid(), TenantId, Guid.NewGuid(), agent.Id);
        existing.AddMessage(new Message(Guid.NewGuid(), MessageRole.User, "上一轮"));
        existing.AddMessage(new Message(Guid.NewGuid(), MessageRole.Agent, "上一轮回复"));
        _conversationRepository.GetByAgentAsync(TenantId, existing.WorkflowId!.Value, agent.Id, Arg.Any<CancellationToken>())
            .Returns(existing);

        var ctx = new WorkflowContext
        {
            WorkflowId = existing.WorkflowId.Value,
            CurrentStepOrder = 0,
            Artifacts = new Dictionary<string, StepArtifact>(),
            Blackboard = Blackboard.Empty,
            Retrieval = RetrievalContext.Empty,
            Summary = StepHistory.Empty,
            TenantId = TenantId
        };

        var sut = CreateSut();
        var result = await sut.ExecuteAsync(CreateNode(agent.Id), ctx, CancellationToken.None);

        // 复用：不新建会话，消息在既有会话上累积
        _conversationRepository.DidNotReceive().Add(Arg.Any<Conversation>());
        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.Equal(4, existing.Messages.Count);
        Assert.Contains("第二次回复", existing.Messages[3].Content);
    }

    [Fact]
    public async Task AgentStep_ConversationPersistenceFailure_Is_NonBlocking()
    {
        var agent = CreateAgent();
        _agentRepository.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        SetupRouterResponse("回复");
        _conversationRepository.GetByAgentAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db down"));

        var sut = CreateSut();
        var result = await sut.ExecuteAsync(CreateNode(agent.Id), ctx: CreateContext(), ct: CancellationToken.None);

        // best-effort（D2=A）：持久化失败只告警，不阻断工作流步骤成功
        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.Equal("回复", result.Output);
    }

    [Fact]
    public async Task AgentStep_ConversationPersistenceCancellation_IsNotSwallowed()
    {
        var agent = CreateAgent();
        _agentRepository.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        SetupRouterResponse("回复");
        _conversationRepository.GetByAgentAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var sut = CreateSut();
        var result = await sut.ExecuteAsync(CreateNode(agent.Id), ctx: CreateContext(), ct: CancellationToken.None);

        // best-effort 只吞普通持久化异常；OperationCanceledException 必须穿透
        // 持久化包裹（转为既有 FailedRetry 语义），不得伪装成 Success。
        Assert.Equal(StepOutcome.FailedRetry, result.Outcome);
    }

    [Fact]
    public async Task AgentStep_ConversationSaveFailure_Detaches_Created_Conversation()
    {
        // F36 三道门修复：创建路径 SaveChanges 失败时，必须把新增会话从共享 change tracker
        // Detach 掉——否则编排器紧随其后的 SaveChangesAsync 会重放同一冲突（典型=唯一过滤
        // 索引并发冲突），把 best-effort「吞掉仅告警」放大成工作流状态保存失败。
        var agent = CreateAgent();
        _agentRepository.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        SetupRouterResponse("回复");
        _conversationRepository.GetByAgentAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Conversation?)null); // 走创建路径
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("unique index conflict"));

        var sut = CreateSut();
        var result = await sut.ExecuteAsync(CreateNode(agent.Id), ctx: CreateContext(), ct: CancellationToken.None);

        // 步骤本身成功（non-blocking），且失败的新增会话已被 Detach 隔离。
        Assert.Equal(StepOutcome.Success, result.Outcome);
        _conversationRepository.Received(1).Detach(Arg.Any<Conversation>());
    }
}