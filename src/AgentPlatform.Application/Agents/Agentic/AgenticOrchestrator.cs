using System.Text;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Tools;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Agents.Agentic;

/// <summary>
/// Drives the ReAct-style autonomous control loop for an agent:
/// <c>plan → act → observe → reflect</c>, where the model proposes tool calls and the
/// platform executes them, feeding results back until the model stops calling tools.
/// This is the primitive that turns an "agent configuration entity" into a real autonomous agent.
/// </summary>
public sealed class AgenticOrchestrator
{
    private readonly IModelClient _modelClient;
    private readonly ToolCallingDispatcher _toolDispatcher;
    private readonly IToolRegistry _toolRegistry;

    /// <summary>Initializes a new instance of the <see cref="AgenticOrchestrator"/> class.</summary>
    public AgenticOrchestrator(
        IModelClient modelClient,
        ToolCallingDispatcher toolDispatcher,
        IToolRegistry toolRegistry)
    {
        _modelClient = modelClient;
        _toolDispatcher = toolDispatcher;
        _toolRegistry = toolRegistry;
    }

    /// <summary>
    /// Runs the agentic control loop for the supplied goal and agent configuration.
    /// </summary>
    /// <param name="goal">The user's objective for the agent.</param>
    /// <param name="agent">The agent aggregate (system prompt, model, allowed tools, iteration cap).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The final answer plus a full execution trace.</returns>
    public async Task<AgenticRunResult> RunGoalAsync(string goal, Agent agent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (string.IsNullOrWhiteSpace(goal)) goal = "(no goal provided)";

        var allowedTools = await ResolveAllowedToolsAsync(agent, ct);
        var allowedNames = new HashSet<string>(allowedTools.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

        var messages = new List<ChatMessage>
        {
            new(MessageRole.System, BuildSystemPrompt(agent, allowedTools)),
            new(MessageRole.User, goal)
        };

        var trace = new List<AgenticTraceStep>();
        var tokensIn = 0;
        var tokensOut = 0;

        for (var i = 1; i <= agent.MaxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();

            var resp = await _modelClient.ChatAsync(agent.ModelEndpoint.ModelName, messages, allowedTools, ct);
            if (resp.TokenUsage is not null)
            {
                tokensIn += resp.TokenUsage.PromptTokens;
                tokensOut += resp.TokenUsage.CompletionTokens;
            }

            // Model believes the task is complete → return the final answer.
            if (resp.ToolCalls is null or { Count: 0 })
            {
                trace.Add(new AgenticTraceStep(i, null, null, resp.Content, true,
                    resp.TokenUsage?.PromptTokens ?? 0, resp.TokenUsage?.CompletionTokens ?? 0, null));
                return new AgenticRunResult(resp.Content, i, trace, tokensIn, tokensOut);
            }

            // Echo the assistant tool-call turn back so the model sees its own proposed calls.
            messages.Add(new ChatMessage(MessageRole.Agent, string.Empty, ToolCalls: resp.ToolCalls));

            foreach (var call in resp.ToolCalls)
            {
                string output;
                bool success;
                string? error = null;

                if (!allowedNames.Contains(call.Name))
                {
                    // Guardrail: never execute a tool outside the agent's allow-list.
                    success = false;
                    output = $"Tool '{call.Name}' is not in this agent's allowed tool list and was not executed.";
                    error = "tool_not_allowed";
                }
                else
                {
                    var result = await _toolDispatcher.DispatchAsync(call.Name, call.ArgumentsJson, ct);
                    success = result.Success;
                    output = result.Output;
                    error = result.ErrorMessage;
                }

                messages.Add(new ChatMessage(MessageRole.Tool, output, ToolCallId: call.Id, ToolName: call.Name));
                trace.Add(new AgenticTraceStep(i, call.Name, call.ArgumentsJson, output, success, 0, 0, error));
            }
        }

        throw new AgentIterationLimitExceededException(agent.MaxIterations);
    }

    private async Task<IReadOnlyList<ToolDefinition>> ResolveAllowedToolsAsync(Agent agent, CancellationToken ct)
    {
        var whitelist = agent.AllowedToolNames;
        if (whitelist.Count == 0) return Array.Empty<ToolDefinition>();

        var all = await _toolRegistry.GetAllAsync(ct);
        return all
            .Where(t => t.IsEnabled && whitelist.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static string BuildSystemPrompt(Agent agent, IReadOnlyList<ToolDefinition> tools)
    {
        var sb = new StringBuilder();
        sb.AppendLine(agent.SystemPrompt);
        sb.AppendLine();
        sb.AppendLine("You are an autonomous agent. Accomplish the user's goal by calling the tools below when they help; " +
                      "when the goal is fully accomplished, respond with the final answer and NO tool calls.");
        if (tools.Count > 0)
        {
            sb.AppendLine("Available tools:");
            foreach (var t in tools)
                sb.AppendLine($"- {t.Name}: {t.Description}");
        }
        else
        {
            sb.AppendLine("No tools are available; rely on your reasoning alone.");
        }
        sb.AppendLine();
        sb.AppendLine("Stop convention: once the task is complete, provide the final answer directly without invoking any tool.");
        return sb.ToString();
    }
}
