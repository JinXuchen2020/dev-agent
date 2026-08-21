using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.ValueObjects;
using AgentPlatform.Infrastructure.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Models;

/// <summary>
/// F29 验收 ①：模型客户端能把 SK 的 FunctionCallContent 解析为平台 ToolCall，
/// 并正确把历史中的 assistant tool_calls / tool 结果往返序列化给模型（declare-only 接线）。
/// </summary>
public class SemanticKernelModelClientToolCallTests
{
    private static readonly ToolDefinition ReadFileTool = new(
        Guid.NewGuid(), "read_file", "Read a file inside the workspace",
        "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}}}", "read_file",
        Guid.NewGuid(), ToolSource.Workspace);

    private static SemanticKernelModelClient CreateClient(IChatCompletionService service) =>
        new(new Dictionary<string, IChatCompletionService> { ["gpt-4o"] = service });

    private static ChatMessageContent AssistantReplyWithToolCall()
    {
        var args = new KernelArguments(new Dictionary<string, object?> { ["path"] = "a.txt" });
        return new ChatMessageContent(AuthorRole.Assistant,
            items: new ChatMessageContentItemCollection
            {
                new FunctionCallContent(functionName: "read_file", id: "call_1", arguments: args)
            });
    }

    [Fact]
    public async Task ChatAsync_WithTools_DeclaresFunctions_And_ParsesToolCalls()
    {
        OpenAIPromptExecutionSettings? capturedSettings = null;
        var service = Substitute.For<IChatCompletionService>();
        service.GetChatMessageContentsAsync(
                Arg.Any<ChatHistory>(),
                Arg.Any<PromptExecutionSettings>(),
                Arg.Any<Kernel>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedSettings = callInfo.ArgAt<PromptExecutionSettings>(1) as OpenAIPromptExecutionSettings;
                return new[] { AssistantReplyWithToolCall() };
            });

        var client = CreateClient(service);
        var messages = new List<ChatMessage>
        {
            new(MessageRole.System, "You are an agent."),
            new(MessageRole.User, "read a.txt")
        };

        var resp = await client.ChatAsync("gpt-4o", messages, new[] { ReadFileTool });

        // ① declare-only 接线：必须把工具以 ToolCallBehavior 声明给模型，且关闭自动执行。
        Assert.NotNull(capturedSettings);
        Assert.NotNull(capturedSettings!.ToolCallBehavior);

        // 模型提议的工具调用被解析为平台 ToolCall。
        Assert.Equal("tool_calls", resp.FinishReason);
        Assert.NotNull(resp.ToolCalls);
        var call = Assert.Single(resp.ToolCalls!);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("read_file", call.Name);
        Assert.Contains("\"a.txt\"", call.ArgumentsJson);
    }

    [Fact]
    public async Task ChatAsync_PlainAnswer_ReturnsNoToolCalls()
    {
        var service = Substitute.For<IChatCompletionService>();
        service.GetChatMessageContentsAsync(
                Arg.Any<ChatHistory>(),
                Arg.Any<PromptExecutionSettings>(),
                Arg.Any<Kernel>(),
                Arg.Any<CancellationToken>())
            .Returns(new[] { new ChatMessageContent(AuthorRole.Assistant, "final answer") });

        var client = CreateClient(service);
        var resp = await client.ChatAsync("gpt-4o",
            new List<ChatMessage> { new(MessageRole.User, "hi") }, new[] { ReadFileTool });

        Assert.Equal("final answer", resp.Content);
        Assert.Null(resp.ToolCalls);
        Assert.Equal("stop", resp.FinishReason);
    }

    [Fact]
    public async Task ChatAsync_ToolCallHistory_RoundTripsToChatHistory()
    {
        ChatHistory? received = null;
        var service = Substitute.For<IChatCompletionService>();
        service.GetChatMessageContentsAsync(
                Arg.Any<ChatHistory>(),
                Arg.Any<PromptExecutionSettings>(),
                Arg.Any<Kernel>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                received = callInfo.ArgAt<ChatHistory>(0);
                return new[] { new ChatMessageContent(AuthorRole.Assistant, "done") };
            });

        var client = CreateClient(service);
        var messages = new List<ChatMessage>
        {
            new(MessageRole.System, "sys"),
            new(MessageRole.User, "goal"),
            new(MessageRole.Agent, string.Empty,
                ToolCalls: new[] { new ToolCall("call_1", "read_file", "{\"path\":\"a.txt\"}") }),
            new(MessageRole.Tool, "file contents", ToolCallId: "call_1", ToolName: "read_file")
        };

        await client.ChatAsync("gpt-4o", messages, new[] { ReadFileTool });

        Assert.NotNull(received);
        Assert.Equal(4, received!.Count);
        // assistant 轮：回显 FunctionCallContent（OpenAI 要求 tool 结果前有 tool_calls）。
        var assistantItems = received[2].Items.OfType<FunctionCallContent>().ToList();
        var echoed = Assert.Single(assistantItems);
        Assert.Equal("read_file", echoed.FunctionName);
        Assert.Equal("call_1", echoed.Id);
        // tool 结果轮：FunctionResultContent 配对 call id。
        var resultItems = received[3].Items.OfType<FunctionResultContent>().ToList();
        var result = Assert.Single(resultItems);
        Assert.Equal("call_1", result.CallId);
        Assert.Equal("read_file", result.FunctionName);
        Assert.Equal("file contents", result.Result as string);
    }
}
