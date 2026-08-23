using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Agents.Agentic;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Application.Tools;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Agentic;

/// <summary>
/// F29 验收 ② / ⑤：ReAct 控制循环 standalone 跑通（Stub 模型 + 内存工具注册表），
/// 以及安全护栏（迭代上限、工具白名单）。
/// </summary>
public class AgenticOrchestratorTests
{
    private readonly IModelRouter _router = Substitute.For<IModelRouter>();
    private readonly ITenantProvider _tenantProvider = Substitute.For<ITenantProvider>();
    private readonly FakeToolRegistry _registry = new();
    private readonly IAgentRoleDefinitionRepository _roleRepo = Substitute.For<IAgentRoleDefinitionRepository>();

    private static readonly ToolDefinition ReadFileTool = new(
        Guid.NewGuid(), "read_file", "Read a file inside the workspace",
        "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}}}", "read_file",
        Guid.NewGuid(), ToolSource.Workspace);

    private static readonly ToolDefinition WriteFileTool = new(
        Guid.NewGuid(), "write_file", "Write a file inside the workspace",
        "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"text\":{\"type\":\"string\"}}}", "write_file",
        Guid.NewGuid(), ToolSource.Workspace);

    private AgenticOrchestrator CreateOrchestrator(params IToolExecutor[] executors)
    {
        var dispatcher = new ToolCallingDispatcher(
            _registry,
            executors,
            NullLogger<ToolCallingDispatcher>.Instance);
        _tenantProvider.GetTenantId().Returns(Guid.NewGuid());
        var workspaceRoot = Substitute.For<IWorkspaceRootProvider>();
        workspaceRoot.WorkspaceRoot.Returns(System.IO.Path.GetTempPath());
        var artifactStore = Substitute.For<IArtifactStore>();
        return new AgenticOrchestrator(_router, _tenantProvider, dispatcher, _registry, workspaceRoot, artifactStore, _roleRepo);
    }

    private static Agent CreateAgent(
        IEnumerable<string>? allowedToolNames = null,
        int maxIterations = 25)
    {
        var tenantId = Guid.NewGuid();
        return new Agent(
            Guid.NewGuid(),
            "test-agent",
            AgentType.Development,
            new ModelEndpoint("openai", "gpt-4o", "https://api.openai.com/v1"),
            "You are a coding agent.",
            tenantId,
            allowedToolNames: allowedToolNames?.ToList() ?? new List<string> { "read_file", "write_file" },
            maxIterations: maxIterations);
    }

    [Fact]
    public async Task RunGoal_ModelProposesToolCalls_Executes_And_ReturnsFinalAnswer()
    {
        _registry.Register(ReadFileTool);
        _registry.Register(WriteFileTool);

        var executor = Substitute.For<IToolExecutor>();
        executor.Source.Returns(ToolSource.Workspace);
        executor.ExecuteAsync(Arg.Any<ToolDefinition>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Ok("file contents"));

        _router.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ModelResponse("", new TokenUsage(10, 20), "gpt-4o", "tool_calls",
                    new[] { new ToolCall("call_1", "read_file", "{\"path\":\"a.txt\"}") }),
                new ModelResponse("Task complete.", new TokenUsage(10, 20), "gpt-4o", "stop"));

        var result = await CreateOrchestrator(executor)
            .RunGoalAsync("read a.txt and summarize", CreateAgent(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("Task complete.", result.FinalAnswer);
        Assert.Equal(2, result.Iterations);
        Assert.Equal(20, result.TotalTokensIn); // 2 x 10 prompt tokens
        Assert.Equal(40, result.TotalTokensOut); // 2 x 20 completion tokens
        Assert.Contains(result.Trace, t => t.ToolName == "read_file" && t.Success);
        Assert.Contains(result.Trace, t => t.ToolName is null && t.Result == "Task complete.");

        // Tool 结果必须回喂模型（Tool role + call id 配对）。
        await _router.Received(2).RouteAsync(
            Arg.Is<RoutingRequest>(r =>
                r.Messages.Any(x => x.Role == MessageRole.Tool && x.ToolCallId == "call_1" && x.ToolName == "read_file")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunGoal_ExceedsMaxIterations_Throws()
    {
        _registry.Register(ReadFileTool);
        var executor = Substitute.For<IToolExecutor>();
        executor.Source.Returns(ToolSource.Workspace);
        executor.ExecuteAsync(Arg.Any<ToolDefinition>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Ok("x"));

        _router.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse("", new TokenUsage(1, 1), "gpt-4o", "tool_calls",
                new[] { new ToolCall("call_1", "read_file", "{}") }));

        // 显式配置有限上限（3）时：模型永不收手 → 必须抛异常而不是死循环。
        // 注意：默认 MaxIterations=0 表示无上限，不会触达此分支。
        var agent = CreateAgent(maxIterations: 3);
        await Assert.ThrowsAsync<AgentIterationLimitExceededException>(
            () => CreateOrchestrator(executor).RunGoalAsync("loop forever", agent, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RunGoal_NoMaxIterations_RunsUntilDone()
    {
        _registry.Register(ReadFileTool);
        var executor = Substitute.For<IToolExecutor>();
        executor.Source.Returns(ToolSource.Workspace);
        executor.ExecuteAsync(Arg.Any<ToolDefinition>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Ok("x"));

        // 模型前两次要求工具调用，第三次收手 → 无上限（0）时必须正常跑完，不抛异常。
        _router.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ModelResponse("", new TokenUsage(1, 1), "gpt-4o", "tool_calls",
                    new[] { new ToolCall("call_1", "read_file", "{}") }),
                new ModelResponse("", new TokenUsage(1, 1), "gpt-4o", "tool_calls",
                    new[] { new ToolCall("call_2", "read_file", "{}") }),
                new ModelResponse("Done at last.", new TokenUsage(1, 1), "gpt-4o", "stop"));

        var agent = CreateAgent(maxIterations: 0);
        var result = await CreateOrchestrator(executor)
            .RunGoalAsync("multi-step task", agent, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("Done at last.", result.FinalAnswer);
        Assert.Equal(3, result.Iterations);
    }

    [Fact]
    public async Task RunGoal_ToolOutsideAllowList_IsNotDispatched()
    {
        _registry.Register(ReadFileTool);
        _registry.Register(WriteFileTool);

        var executor = Substitute.For<IToolExecutor>();
        executor.Source.Returns(ToolSource.Workspace);
        executor.ExecuteAsync(Arg.Any<ToolDefinition>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ToolExecutionResult.Ok("should not happen"));

        // Agent 白名单只允许 read_file —— 模型提议 write_file 必须被拦下。
        _router.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ModelResponse("", new TokenUsage(1, 1), "gpt-4o", "tool_calls",
                    new[] { new ToolCall("call_1", "write_file", "{\"path\":\"b.txt\"}") }),
                new ModelResponse("Done.", new TokenUsage(1, 1), "gpt-4o", "stop"));

        var agent = CreateAgent(allowedToolNames: new[] { "read_file" });
        var result = await CreateOrchestrator(executor)
            .RunGoalAsync("write b.txt", agent, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("Done.", result.FinalAnswer);
        var blocked = Assert.Single(result.Trace, t => t.ToolName == "write_file");
        Assert.False(blocked.Success);
        Assert.Equal("tool_not_allowed", blocked.Error);
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolDefinition>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunGoal_NoAllowedTools_PassesEmptyTools_And_StopsOnPlainAnswer()
    {
        _router.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse("Reasoning only.", new TokenUsage(5, 5), "gpt-4o", "stop"));

        var agent = CreateAgent(allowedToolNames: Array.Empty<string>());
        var result = await CreateOrchestrator()
            .RunGoalAsync("answer without tools", agent, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("Reasoning only.", result.FinalAnswer);
        Assert.Equal(1, result.Iterations);
        await _router.Received(1).RouteAsync(
            Arg.Is<RoutingRequest>(r => r.Tools != null && r.Tools.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunGoal_RoleSystemPrompt_PrependedBeforeAgentPrompt()
    {
        string? capturedSystem = null;
        _router.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var req = (RoutingRequest)ci[0]!;
                capturedSystem = req.Messages.FirstOrDefault(m => m.Role == MessageRole.System)?.Content;
                return new ModelResponse("Done.", new TokenUsage(1, 1), "gpt-4o", "stop");
            });

        // 角色定义（DB 权威）里带系统提示词：按 agent.Role.RoleCode="development" 命中。
        const string rolePrompt = "You are a senior backend engineer focused on correctness.";
        _roleRepo.GetByRoleCodeAsync("development", Arg.Any<CancellationToken>())
            .Returns(new AgentRoleDefinition(Guid.NewGuid(), "Developer", "development", "代码实现", rolePrompt));

        var agent = CreateAgent(); // 默认 Role = AgentType.Development
        await CreateOrchestrator().RunGoalAsync("do something", agent, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(capturedSystem);
        var roleIdx = capturedSystem!.IndexOf(rolePrompt, StringComparison.Ordinal);
        var agentIdx = capturedSystem.IndexOf("You are a coding agent.", StringComparison.Ordinal);
        Assert.True(roleIdx >= 0, "角色提示词应被注入");
        Assert.True(agentIdx > roleIdx, "角色提示词应在 agent 自定义提示词之前");
    }

    [Fact]
    public async Task RunGoal_NoRoleDefinition_DoesNotPrepend()
    {
        string? capturedSystem = null;
        _router.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var req = (RoutingRequest)ci[0]!;
                capturedSystem = req.Messages.FirstOrDefault(m => m.Role == MessageRole.System)?.Content;
                return new ModelResponse("Done.", new TokenUsage(1, 1), "gpt-4o", "stop");
            });

        // 角色定义查不到 → 不应有任何角色提示词注入，system prompt 以 agent 自定义开头。
        _roleRepo.GetByRoleCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((AgentRoleDefinition?)null);

        var agent = CreateAgent();
        await CreateOrchestrator().RunGoalAsync("do something", agent, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(capturedSystem);
        Assert.StartsWith("You are a coding agent.", capturedSystem, StringComparison.Ordinal);
    }

    private sealed class FakeToolRegistry : IToolRegistry
    {
        private readonly List<ToolDefinition> _tools = new();

        public void Register(ToolDefinition tool, CancellationToken ct = default) => _tools.Add(tool);

        public Task<ToolDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_tools.FirstOrDefault(t => t.Id == id));

        public Task<ToolDefinition?> GetByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(_tools.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ToolDefinition>>(_tools.ToList());

        public void Unregister(Guid id, CancellationToken ct = default) => _tools.RemoveAll(t => t.Id == id);
    }
}
