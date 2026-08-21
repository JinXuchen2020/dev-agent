using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Agents.Agentic;
using AgentPlatform.Application.Tools;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;
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
    private readonly IModelClient _modelClient = Substitute.For<IModelClient>();
    private readonly FakeToolRegistry _registry = new();

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
        return new AgenticOrchestrator(_modelClient, dispatcher, _registry);
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

        _modelClient.ChatAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ChatMessage>>(),
                Arg.Any<IReadOnlyList<ToolDefinition>>(),
                Arg.Any<CancellationToken>())
            .Returns(
                new ModelResponse("", new TokenUsage(10, 20), "gpt-4o", "tool_calls",
                    new[] { new ToolCall("call_1", "read_file", "{\"path\":\"a.txt\"}") }),
                new ModelResponse("Task complete.", new TokenUsage(10, 20), "gpt-4o", "stop"));

        var result = await CreateOrchestrator(executor)
            .RunGoalAsync("read a.txt and summarize", CreateAgent(), CancellationToken.None);

        Assert.Equal("Task complete.", result.FinalAnswer);
        Assert.Equal(2, result.Iterations);
        Assert.Equal(20, result.TotalTokensIn); // 2 x 10 prompt tokens
        Assert.Equal(40, result.TotalTokensOut); // 2 x 20 completion tokens
        Assert.Contains(result.Trace, t => t.ToolName == "read_file" && t.Success);
        Assert.Contains(result.Trace, t => t.ToolName is null && t.Result == "Task complete.");

        // Tool 结果必须回喂模型（Tool role + call id 配对）。
        await _modelClient.Received(2).ChatAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<ChatMessage>>(m =>
                m.Any(x => x.Role == MessageRole.Tool && x.ToolCallId == "call_1" && x.ToolName == "read_file")),
            Arg.Any<IReadOnlyList<ToolDefinition>>(),
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

        _modelClient.ChatAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatMessage>>(),
                Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse("", new TokenUsage(1, 1), "gpt-4o", "tool_calls",
                new[] { new ToolCall("call_1", "read_file", "{}") }));

        // 迭代上限硬保护：模型永不收手 → 必须抛异常而不是死循环。
        var agent = CreateAgent(maxIterations: 3);
        await Assert.ThrowsAsync<AgentIterationLimitExceededException>(
            () => CreateOrchestrator(executor).RunGoalAsync("loop forever", agent, CancellationToken.None));
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
        _modelClient.ChatAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatMessage>>(),
                Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(
                new ModelResponse("", new TokenUsage(1, 1), "gpt-4o", "tool_calls",
                    new[] { new ToolCall("call_1", "write_file", "{\"path\":\"b.txt\"}") }),
                new ModelResponse("Done.", new TokenUsage(1, 1), "gpt-4o", "stop"));

        var agent = CreateAgent(allowedToolNames: new[] { "read_file" });
        var result = await CreateOrchestrator(executor)
            .RunGoalAsync("write b.txt", agent, CancellationToken.None);

        Assert.Equal("Done.", result.FinalAnswer);
        var blocked = Assert.Single(result.Trace, t => t.ToolName == "write_file");
        Assert.False(blocked.Success);
        Assert.Equal("tool_not_allowed", blocked.Error);
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ToolDefinition>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunGoal_NoAllowedTools_PassesEmptyTools_And_StopsOnPlainAnswer()
    {
        _modelClient.ChatAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatMessage>>(),
                Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse("Reasoning only.", new TokenUsage(5, 5), "gpt-4o", "stop"));

        var agent = CreateAgent(allowedToolNames: Array.Empty<string>());
        var result = await CreateOrchestrator()
            .RunGoalAsync("answer without tools", agent, CancellationToken.None);

        Assert.Equal("Reasoning only.", result.FinalAnswer);
        Assert.Equal(1, result.Iterations);
        await _modelClient.Received(1).ChatAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<ChatMessage>>(),
            Arg.Is<IReadOnlyList<ToolDefinition>>(t => t.Count == 0),
            Arg.Any<CancellationToken>());
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
