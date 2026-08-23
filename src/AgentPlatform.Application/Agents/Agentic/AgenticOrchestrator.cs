using System.Runtime.CompilerServices;
using System.Text;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Application.Tools;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;

namespace AgentPlatform.Application.Agents.Agentic;

/// <summary>
/// Drives the ReAct-style autonomous control loop for an agent:
/// <c>plan → act → observe → reflect</c>, where the model proposes tool calls and the
/// platform executes them, feeding results back until the model stops calling tools.
/// This is the primitive that turns an "agent configuration entity" into a real autonomous agent.
/// </summary>
public sealed class AgenticOrchestrator
{
    private readonly IModelRouter _router;
    private readonly ITenantProvider _tenantProvider;
    private readonly ToolCallingDispatcher _toolDispatcher;
    private readonly IToolRegistry _toolRegistry;
    private readonly IWorkspaceRootProvider _workspaceRoot;
    private readonly IArtifactStore _artifactStore;
    private readonly IAgentRoleDefinitionRepository _roleDefinitionRepository;

    /// <summary>Initializes a new instance of the <see cref="AgenticOrchestrator"/> class.</summary>
    public AgenticOrchestrator(
        IModelRouter router,
        ITenantProvider tenantProvider,
        ToolCallingDispatcher toolDispatcher,
        IToolRegistry toolRegistry,
        IWorkspaceRootProvider workspaceRoot,
        IArtifactStore artifactStore,
        IAgentRoleDefinitionRepository roleDefinitionRepository)
    {
        _router = router;
        _tenantProvider = tenantProvider;
        _toolDispatcher = toolDispatcher;
        _toolRegistry = toolRegistry;
        _workspaceRoot = workspaceRoot;
        _artifactStore = artifactStore;
        _roleDefinitionRepository = roleDefinitionRepository;
    }

    /// <summary>
    /// Runs the agentic control loop for the supplied goal and agent configuration.
    /// </summary>
    /// <param name="goal">The user's objective for the agent.</param>
    /// <param name="agent">The agent aggregate (system prompt, model, allowed tools, iteration cap).</param>
    /// <param name="runId">A caller-supplied identifier used to scope and persist run artifacts.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The final answer plus a full execution trace.</returns>
    public async Task<AgenticRunResult> RunGoalAsync(string goal, Agent agent, Guid runId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (string.IsNullOrWhiteSpace(goal)) goal = "(no goal provided)";

        var allowedTools = await ResolveAllowedToolsAsync(agent, ct);
        var allowedNames = new HashSet<string>(allowedTools.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

        var messages = new List<ChatMessage>
        {
            new(MessageRole.System, await BuildSystemPromptAsync(agent, allowedTools, ct)),
            new(MessageRole.User, goal)
        };

        var trace = new List<AgenticTraceStep>();
        var tokensIn = 0;
        var tokensOut = 0;

        // 通过 ModelRouter 路由：优先使用租户在「我的凭据」中添加的 BYO 模型客户端，
        // 否则回退到平台级模型。运行环境一律真实调用，未配置 provider 时由底层抛出明确错误。
        var routeRequest = new RoutingRequest(
            _tenantProvider.GetTenantId(),
            messages,
            agent.ModelEndpoint.ModelName,
            allowedTools);

        for (var i = 1; agent.MaxIterations <= 0 || i <= agent.MaxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();

            var resp = await _router.RouteAsync(routeRequest, ct);
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
                var artifacts = await SnapshotArtifactsAsync(runId, ct);
                return new AgenticRunResult(resp.Content, i, trace, tokensIn, tokensOut, Artifacts: artifacts);
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
                    try
                    {
                        // 编排层兜底：任何沙箱/执行器 60s 内未返回都按失败处理（各沙箱自身还有更短的
                        // 超时，这里是最后防线），保证任务一定推进（tool_result 事件必达），不无限挂起。
                        var result = await _toolDispatcher.DispatchAsync(call.Name, call.ArgumentsJson, ct)
                            .WaitAsync(TimeSpan.FromSeconds(60), ct);
                        success = result.Success;
                        output = result.Output;
                        error = result.ErrorMessage;
                    }
                    catch (TimeoutException)
                    {
                        success = false;
                        output = $"Tool '{call.Name}' timed out after 60s.";
                        error = "tool_timeout";
                    }
                }

                messages.Add(new ChatMessage(MessageRole.Tool, output, ToolCallId: call.Id, ToolName: call.Name));
                trace.Add(new AgenticTraceStep(i, call.Name, call.ArgumentsJson, output, success, 0, 0, error));
            }
        }

        // 仅当配置了有限上限（MaxIterations > 0）时才可能触达此处；无上限（<=0）时循环随
        // 模型返回「完成」自然结束，不会抛错。
        if (agent.MaxIterations > 0)
            throw new AgentIterationLimitExceededException(agent.MaxIterations);
        // 编译器兜底：无上限且模型始终不返回完成信号时，循环理论上无限执行（由外部
        // cancellation 或 stopCriteria 终止），此处不可达。
        throw new AgentIterationLimitExceededException(agent.MaxIterations);
    }

    /// <summary>
    /// Runs the agentic control loop and yields progress events as they happen, so a UI can render the
    /// "thinking process" (tool calls + results) in real time and stream the final answer token-by-token.
    /// Intermediate iterations use <see cref="IModelRouter.RouteAsync"/> (to detect tool calls); the final
    /// answer is streamed via <see cref="IModelRouter.RouteStreamAsync"/>.
    /// </summary>
    public async IAsyncEnumerable<AgenticStreamEvent> RunGoalStreamAsync(
        string goal, Agent agent, Guid runId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 主循环逻辑在 RunGoalStreamCoreAsync 中（C# 不允许在含 catch 的 try 体内 yield return，
        // 因此异常兜底放在 SSE 端点：由控制器捕获并写一条 error 事件，让取消自然传播）。
        await foreach (var ev in RunGoalStreamCoreAsync(goal, agent, runId, ct))
        {
            yield return ev;
        }
    }

    private async IAsyncEnumerable<AgenticStreamEvent> RunGoalStreamCoreAsync(
        string goal, Agent agent, Guid runId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (string.IsNullOrWhiteSpace(goal)) goal = "(no goal provided)";

        var allowedTools = await ResolveAllowedToolsAsync(agent, ct);
        var allowedNames = new HashSet<string>(allowedTools.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

        var messages = new List<ChatMessage>
        {
            new(MessageRole.System, await BuildSystemPromptAsync(agent, allowedTools, ct)),
            new(MessageRole.User, goal)
        };

        var tokensIn = 0;
        var tokensOut = 0;

        var routeRequest = new RoutingRequest(
            _tenantProvider.GetTenantId(),
            messages,
            agent.ModelEndpoint.ModelName,
            allowedTools);

        // 无可用工具 → 模型不可能提议工具调用，首轮响应即最终答案，直接流式输出（一次调用）。
        if (allowedTools.Count == 0)
        {
            yield return new AgenticStreamEvent("iteration", Iteration: 1);
            var sb = new StringBuilder();
            await foreach (var delta in _router.RouteStreamAsync(routeRequest, ct))
            {
                sb.Append(delta);
                yield return new AgenticStreamEvent("answer_delta", Delta: delta);
            }

            var artifactsNoTools = await SnapshotArtifactsAsync(runId, ct);
            yield return new AgenticStreamEvent("done", FinalAnswer: sb.ToString(), Iteration: 1, TokensIn: tokensIn, TokensOut: tokensOut, Artifacts: artifactsNoTools);
            yield break;
        }

        for (var i = 1; agent.MaxIterations <= 0 || i <= agent.MaxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            yield return new AgenticStreamEvent("iteration", Iteration: i);

            var resp = await _router.RouteAsync(routeRequest, ct);
            if (resp.TokenUsage is not null)
            {
                tokensIn += resp.TokenUsage.PromptTokens;
                tokensOut += resp.TokenUsage.CompletionTokens;
            }

            // 模型认为任务完成（无工具调用）→ 流式输出最终答案。
            // 上方非流响应已确认无工具调用；此处再用流式路径重跑一次以逐 token 返回（仅末轮多一次补全）。
            if (resp.ToolCalls is null or { Count: 0 })
            {
                var sb = new StringBuilder();
                await foreach (var delta in _router.RouteStreamAsync(routeRequest, ct))
                {
                    sb.Append(delta);
                    yield return new AgenticStreamEvent("answer_delta", Delta: delta);
                }

                var artifacts = await SnapshotArtifactsAsync(runId, ct);
                yield return new AgenticStreamEvent("done", FinalAnswer: sb.ToString(), Iteration: i, TokensIn: tokensIn, TokensOut: tokensOut, Artifacts: artifacts);
                yield break;
            }

            // Echo the assistant tool-call turn back so the model sees its own proposed calls.
            messages.Add(new ChatMessage(MessageRole.Agent, string.Empty, ToolCalls: resp.ToolCalls));

            foreach (var call in resp.ToolCalls)
            {
                yield return new AgenticStreamEvent("tool_call", Iteration: i, ToolName: call.Name, ArgumentsJson: call.ArgumentsJson);

                string output;
                bool success;
                string? error = null;

                if (!allowedNames.Contains(call.Name))
                {
                    success = false;
                    output = $"Tool '{call.Name}' is not in this agent's allowed tool list and was not executed.";
                    error = "tool_not_allowed";
                }
                else
                {
                    try
                    {
                        // 编排层兜底：任何沙箱/执行器 60s 内未返回都按失败处理（各沙箱自身还有更短的
                        // 超时，这里是最后防线），保证任务一定推进（tool_result 事件必达），不无限挂起。
                        var result = await _toolDispatcher.DispatchAsync(call.Name, call.ArgumentsJson, ct)
                            .WaitAsync(TimeSpan.FromSeconds(60), ct);
                        success = result.Success;
                        output = result.Output;
                        error = result.ErrorMessage;
                    }
                    catch (TimeoutException)
                    {
                        success = false;
                        output = $"Tool '{call.Name}' timed out after 60s.";
                        error = "tool_timeout";
                    }
                }

                messages.Add(new ChatMessage(MessageRole.Tool, output, ToolCallId: call.Id, ToolName: call.Name));
                yield return new AgenticStreamEvent("tool_result", Iteration: i, ToolName: call.Name, Output: output, Success: success);
            }
        }

        // 仅当配置了有限上限（MaxIterations > 0）时才可能触达此处；无上限（<=0）时循环随
        // 模型返回「完成」自然结束，不会抛错。
        if (agent.MaxIterations > 0)
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

    // 把本次 run 的临时工作区快照为持久化产物（best-effort：失败返回空列表，不影响 run 结果）。
    private async Task<IReadOnlyList<ArtifactEntry>> SnapshotArtifactsAsync(Guid runId, CancellationToken ct)
    {
        try
        {
            return await _artifactStore.SnapshotAsync(runId, _workspaceRoot.WorkspaceRoot, ct);
        }
        catch (Exception ex)
        {
            // 编排层吞掉产物异常：artifacts 是增强能力，绝不应阻断 done 事件。
            Console.Error.WriteLine($"[AgenticOrchestrator] 产物快照失败 runId={runId}: {ex.Message}");
            return Array.Empty<ArtifactEntry>();
        }
    }

    /// <summary>
    /// Composes the system prompt for the agentic run. The role definition's system prompt (if any)
    /// is used as the baseline identity/responsibility layer, followed by the agent's own system prompt
    /// (the user's concrete instructions), then the fixed scaffolding (tool list, workspace note, stop
    /// convention). The role prompt establishes WHO the agent is; the agent prompt tells it WHAT to do.
    /// </summary>
    private async Task<string> BuildSystemPromptAsync(Agent agent, IReadOnlyList<ToolDefinition> tools, CancellationToken ct)
    {
        var sb = new StringBuilder();

        // 角色基线提示词：从 AgentRoleDefinition 取，DB 权威。角色提示词在前，定义智能体身份与职责边界。
        if (!string.IsNullOrWhiteSpace(agent.Role?.RoleCode))
        {
            var roleDef = await _roleDefinitionRepository.GetByRoleCodeAsync(agent.Role.RoleCode, ct);
            if (!string.IsNullOrWhiteSpace(roleDef?.SystemPrompt))
            {
                sb.AppendLine(roleDef.SystemPrompt.Trim());
                sb.AppendLine();
            }
        }

        // 智能体自定义提示词：用户的具体指令，叠加在角色基线之上。
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
        sb.AppendLine("Workspace note: the workspace tools operate in a per-run temporary directory whose " +
                      "exact path varies (e.g. /tmp/ap_workspace_*). Run `pwd` first to confirm the current " +
                      "directory and use relative paths for file operations. Never assume a fixed path like " +
                      "/workspace.");
        sb.AppendLine();
        sb.AppendLine("Stop convention: once the task is complete, provide the final answer directly without invoking any tool.");
        return sb.ToString();
    }
}
