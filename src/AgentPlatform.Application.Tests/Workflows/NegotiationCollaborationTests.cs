using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Domain.ValueObjects;
using AgentPlatform.Infrastructure.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Workflows;

/// <summary>
/// F32 验收：Negotiation 协作模式——双 agent 真并发提案（时间窗重叠实证）、
/// critic 拒绝触发 Critique+Handoff（上下文随移交传递）、消息预算熔断、
/// 以及无绑定 agent 时诚实降级到既有串行循环。
/// </summary>
public sealed class NegotiationCollaborationTests
{
    private readonly IWorkflowRepository _repository = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDomainEventBus _eventBus = Substitute.For<IDomainEventBus>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
    private readonly ILogger<NegotiationOrchestrator> _logger = Substitute.For<ILogger<NegotiationOrchestrator>>();
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly ITokenCounter _tokenCounter = Substitute.For<ITokenCounter>();

    // 协作基础设施
    private readonly IAgentMessageBus _bus = Substitute.For<IAgentMessageBus>();
    private readonly IAgentMessageLogRepository _logRepo = Substitute.For<IAgentMessageLogRepository>();
    private readonly IAgentRepository _agentRepository = Substitute.For<IAgentRepository>();
    private readonly IModelRouter _router = Substitute.For<IModelRouter>();

    public NegotiationCollaborationTests()
    {
        _logRepo.ExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        _logRepo.TryMarkConsumedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _logRepo.GetUnconsumedByWorkflowAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<AgentPlatform.Domain.Aggregates.AgentMessages.AgentMessageLog>());
    }

    private NegotiationOrchestrator CreateSut(AgentCollaborationSettings? settings = null)
    {
        _serviceProvider.GetService(typeof(IAgentMessageBus)).Returns(_bus);
        _serviceProvider.GetService(typeof(IAgentMessageLogRepository)).Returns(_logRepo);
        _serviceProvider.GetService(typeof(IAgentRepository)).Returns(_agentRepository);
        _serviceProvider.GetService(typeof(IModelRouter)).Returns(_router);
        _serviceProvider.GetService(typeof(IOptions<AgentCollaborationSettings>))
            .Returns(Options.Create(settings ?? new AgentCollaborationSettings()));

        // 注意：IServiceScopeFactory 由各测试的 SetupSelection 配置（提供选择/终止策略），
        // 这里不得覆盖——协作门禁与 legacy 循环都从 scope 解析它们。

        return new NegotiationOrchestrator(
            _repository, _unitOfWork, _eventBus, _serviceProvider,
            _logger, DefaultSettings(), _vectorStore, _tokenCounter);
    }

    internal static StateMachineSettings DefaultSettings() => new()
    {
        MaxRetryAttempts = 2,
        StepTimeoutSeconds = 30,
        RetryDelayMs = 1,
        MaxSummaryTokens = 8000
    };

    private static Workflow CreateWorkflow(params string[] stepNames)
    {
        var wf = new Workflow(Guid.NewGuid(), "f32-collab", Guid.NewGuid());
        for (var i = 0; i < stepNames.Length; i++)
            wf.AddStep(new WorkflowStep(Guid.NewGuid(), i, stepNames[i]));
        return wf;
    }

    private static Agent CreateAgent(string name) => new(
        Guid.NewGuid(), name,
        new AgentType("architecture", "系统架构", "架构师"),
        new ModelEndpoint("DeepSeek", "deepseek-chat", "https://api.deepseek.com/v1"),
        $"你是{name}。", Guid.NewGuid());

    private void SetupSelection(ISelectionStrategy selection, ITerminationCondition termination)
    {
        var scopedProvider = Substitute.For<IServiceProvider>();
        scopedProvider.GetService(typeof(ISelectionStrategy)).Returns(selection);
        scopedProvider.GetService(typeof(ITerminationCondition)).Returns(termination);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(scopedProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);
        _serviceProvider.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);

        // executor 解析（critic 相位走 ExecuteStepWithRetryAsync）
        var executors = Array.Empty<IStepExecutor>();
        _serviceProvider.GetService(typeof(IEnumerable<IStepExecutor>)).Returns(executors);
        scopedProvider.GetService(typeof(IEnumerable<IStepExecutor>)).Returns(executors);
    }

    [Fact]
    public async Task TwoBoundAgents_Propose_TrulyInParallel()
    {
        // 时间窗重叠实证：两个 RouteAsync 同时在场才置 overlap。
        // 用 Task.Run 包裹模拟真实网络延迟——否则 NSubstitute 返回已完成 Task，
        // 提案会在首个 await 前同步跑完，WhenAll 蜕化为串行（测不出并行）。
        var current = 0;
        var overlapSeen = false;
        _router.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.Run(() =>
            {
                Interlocked.Increment(ref current);
                if (Volatile.Read(ref current) >= 2) overlapSeen = true;
                Thread.Sleep(150); // 拉开窗口，串行执行不可能重叠
                Interlocked.Decrement(ref current);
                return new ModelResponse($"proposal-{Guid.NewGuid():N}", null, "deepseek-chat", "stop");
            }));

        var wf = CreateWorkflow("arch-a", "arch-b");
        var ax = CreateAgent("架构A");
        var ay = CreateAgent("架构B");
        wf.Steps[0].AssignAgent(ax.Id);
        wf.Steps[1].AssignAgent(ay.Id);
        _agentRepository.GetByIdAsync(ax.Id, Arg.Any<CancellationToken>()).Returns(ax);
        _agentRepository.GetByIdAsync(ay.Id, Arg.Any<CancellationToken>()).Returns(ay);

        var selection = Substitute.For<ISelectionStrategy>(); // 提案后无剩余 → null 触发完成
        selection.SelectNextAsync(Arg.Any<WorkflowContext>(), Arg.Any<IReadOnlyList<WorkflowStep>>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowStep?)null);
        var termination = Substitute.For<ITerminationCondition>();
        termination.ShouldTerminateAsync(Arg.Any<WorkflowContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        SetupSelection(selection, termination);

        await CreateSut().RunNegotiationAsync(wf, CancellationToken.None);

        Assert.True(overlapSeen, "two proposals must overlap in time (true parallelism)");
        Assert.All(wf.Steps, s => Assert.Equal(WorkflowState.Completed, s.State));
        Assert.Equal(WorkflowState.Completed, wf.CurrentState);
        // 双 Proposal 落总线
        await _bus.Received(2).PublishAsync(Arg.Any<AgentMessage>(), wf.TenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CriticRejection_Emits_Critique_And_Handoff_ToOtherAgent()
    {
        // A 成功提案；B 绑定的 agent 缺失 → B 保持 Pending；critic 拒绝 → Handoff 定向给 B 的 agent
        var ax = CreateAgent("作者");
        var ay = CreateAgent("接手者");

        var wf = CreateWorkflow("arch", "backup", "critic-gate");
        wf.Steps[0].AssignAgent(ax.Id);
        wf.Steps[1].AssignAgent(ay.Id);
        _agentRepository.GetByIdAsync(ax.Id, Arg.Any<CancellationToken>()).Returns(ax);
        _agentRepository.GetByIdAsync(ay.Id, Arg.Any<CancellationToken>()).Returns((Agent?)null); // B 的 agent 缺失

        var rejectionJson = "{\"Approved\":false,\"Feedback\":\"需要重做\"}";
        _router.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse("ok-proposal", null, "deepseek-chat", "stop"));

        var callIndex = 0;
        var selection = Substitute.For<ISelectionStrategy>();
        selection.SelectNextAsync(Arg.Any<WorkflowContext>(), Arg.Any<IReadOnlyList<WorkflowStep>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                Interlocked.Increment(ref callIndex);
                // 第一轮提案后选择出 critic 步骤执行并拒绝；之后选 null 收敛退出
                return callIndex == 1 ? wf.Steps[2] : null;
            });
        var termination = Substitute.For<ITerminationCondition>();
        termination.ShouldTerminateAsync(Arg.Any<WorkflowContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        SetupSelection(selection, termination);

        // 用 ConfigurableStepExecutor 风格的桩执行 critic 步骤：返回拒绝 JSON
        var criticExecutor = Substitute.For<IStepExecutor>();
        criticExecutor.HandlesType.Returns(StepType.Critic);
        criticExecutor.StepType.Returns("*critic*");
        criticExecutor.ExecuteAsync(Arg.Any<IWorkflowExecutable>(), Arg.Any<WorkflowContext>(), Arg.Any<CancellationToken>())
            .Returns(StepExecutionResult.Success(rejectionJson, rejectionJson));
        _serviceProvider.GetService(typeof(IEnumerable<IStepExecutor>)).Returns(new[] { criticExecutor });

        var published = new List<AgentMessage>();
        _bus.PublishAsync(Arg.Do<AgentMessage>(published.Add), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await CreateSut().RunNegotiationAsync(wf, CancellationToken.None);

        var critique = published.Single(m => m.Type == AgentMessageType.Critique);
        var handoff = published.Single(m => m.Type == AgentMessageType.Handoff);
        Assert.Equal(ax.Id, critique.ReceiverId);           // 回被拒作者
        Assert.Equal(ay.Id, handoff.ReceiverId);            // 移交给其他绑定 agent
        Assert.Contains("重做", handoff.Payload);            // 上下文随移交传递
    }

    [Fact]
    public async Task MessageBudget_Exceeded_CircuitBreaks_ToPaused()
    {
        var ax = CreateAgent("唯一");
        var wf = CreateWorkflow("solo");
        wf.Steps[0].AssignAgent(ax.Id);
        _agentRepository.GetByIdAsync(ax.Id, Arg.Any<CancellationToken>()).Returns(ax);

        _router.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse("p", null, "deepseek-chat", "stop"));
        var settings = new AgentCollaborationSettings { MaxMessagesPerRound = 0 }; // 预算 0 → 发布即熔断

        SetupSelection(Substitute.For<ISelectionStrategy>(), Substitute.For<ITerminationCondition>());

        await CreateSut(settings).RunNegotiationAsync(wf, CancellationToken.None);

        // 熔断：工作流 Paused + 告警（验收 4），而非挂死
        Assert.Equal(WorkflowState.Paused, wf.CurrentState);
    }
}