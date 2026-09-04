using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Shared;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// Executes a workflow step/node by invoking the LLM through <see cref="IModelRouter"/>
/// (tenant BYO credentials first, platform catalog fallback, candidate degradation — F31 ②).
/// When the node binds an agent (<see cref="IWorkflowExecutable.AssignedAgentId"/>), the agent
/// aggregate is loaded at execution time and its <c>SystemPrompt</c> drives the prompt while its
/// <c>ModelEndpoint.ModelName</c> becomes the routing preference (F31 ① runtime materialization).
/// Unbound nodes keep the legacy generic prompt and route without a model preference (acceptance #5).
/// <para>
/// F36 上下文隔离：(1) prompt 注入的 Blackboard 视图按 agent 软分区——agent 步骤只见「全局共享区 +
/// 自己分区」（<c>agent:{agentId}:*</c>），未绑定 agent 的 LLM 步骤见全局区；(2) agent 步骤自动
/// 创建/复用 per-agent per-workflow 的 <see cref="Conversation"/> 并写入本轮 prompt/回复消息
/// （best-effort，持久化失败不阻断编排）；(3) 最终回复显式回写全局键 <c>agent:{agentId}:output</c>
/// 供下游步骤引用。
/// </para>
/// Falls back to any node whose <see cref="IStepExecutor.HandlesType"/> is not explicitly handled.
/// </summary>
internal sealed class AgentCallStepExecutor : IStepExecutor
{
    private readonly ILogger<AgentCallStepExecutor> _logger;
    private readonly IAgentRepository _agentRepository;
    private readonly IModelRouter _modelRouter;
    private readonly IConversationRepository _conversationRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AgentCallStepExecutor(
        ILogger<AgentCallStepExecutor> logger,
        IAgentRepository agentRepository,
        IModelRouter modelRouter,
        IConversationRepository conversationRepository,
        IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _agentRepository = agentRepository;
        _modelRouter = modelRouter;
        _conversationRepository = conversationRepository;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Legacy glob fallback — matches any step name.</summary>
    public string StepType => "*";

    /// <summary>Handles LLM-type nodes explicitly.</summary>
    public StepType? HandlesType => AgentPlatform.Domain.Enums.StepType.LLM;

    public async Task<StepExecutionResult> ExecuteAsync(IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(ctx);

        _logger.LogInformation("Executing step: {StepName} (workflow: {WorkflowId})",
            step.Name, ctx.WorkflowId);

        try
        {
            // F31 ①: resolve the bound agent at execution time. The repository's EF query filter
            // enforces tenant isolation, so a cross-tenant id surfaces as not-found (fail-loud below)
            // instead of leaking another tenant's agent configuration.
            Agent? agent = null;
            if (step.AssignedAgentId.HasValue)
            {
                agent = await _agentRepository.GetByIdAsync(step.AssignedAgentId.Value, ct);
                if (agent is null)
                {
                    var missing = $"节点 '{step.Name}' 绑定的智能体 {step.AssignedAgentId.Value} 不存在或当前租户无权访问。请检查节点绑定或重新保存工作流。";
                    _logger.LogError("Step {StepName}: bound agent {AgentId} not found for tenant — failing loud",
                        step.Name, step.AssignedAgentId.Value);
                    return StepExecutionResult.RetryableFailure(missing);
                }
                _logger.LogInformation("Step {StepName} materialized agent {AgentName} (model: {Provider}/{Model})",
                    step.Name, agent.Name, agent.ModelEndpoint.Provider, agent.ModelEndpoint.ModelName);
            }

            var messages = BuildPrompt(step, ctx, agent);

            // F31 ②: route through ModelRouter — BYO credentials take priority, then the platform
            // catalog with candidate fallback. PreferredModel boosts the agent's own model to the
            // front of the candidate list without hard-failing when it is unavailable.
            var preferredModel = agent?.ModelEndpoint.ModelName;
            var request = new RoutingRequest(ctx.TenantId, messages, PreferredModel: preferredModel);
            var response = await _modelRouter.RouteAsync(request, ct);

            var output = response.Content;
            var artifact = JsonSerializer.Serialize(new
            {
                step = step.Name,
                agent = agent?.Name,
                output = Truncate(output, 500)
            });

            if (agent is not null)
            {
                // F36 D4=A：最终回复显式回写全局键，下游步骤可经 Blackboard.Get 显式引用；
                // 键名带 agentId，其他 agent 的分区视图读不到（非自身分区键一律过滤）。
                ctx.Blackboard.Set(Blackboard.AgentOutputKey(agent.Id), Truncate(output, 8000));

                // F36 D2=A：agent 步骤的对话历史落 per-agent per-workflow 会话（创建/复用 + 写入
                // prompt 摘要与回复）。best-effort：持久化失败仅告警，不阻断工作流执行。
                try
                {
                    await PersistAgentConversationAsync(agent, ctx, step.Name, messages, output, response.TokenUsage, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Step {StepName}: failed to persist agent conversation for agent {AgentId} (non-blocking)",
                        step.Name, agent.Id);
                }
            }

            _logger.LogInformation("Step {StepName} completed via model {ModelId} (tokens: {Tokens})",
                step.Name, response.ModelId, response.TokenUsage?.TotalTokens ?? 0);
            return StepExecutionResult.Success(output, artifact, tokenUsage: response.TokenUsage);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Step {StepName} was cancelled", step.Name);
            return StepExecutionResult.RetryableFailure("Step execution was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step {StepName} failed: {Message}", step.Name, ex.Message);
            return StepExecutionResult.RetryableFailure(ex.Message);
        }
    }

    private List<ChatMessage> BuildPrompt(IWorkflowExecutable step, WorkflowContext ctx, Agent? agent)
    {
        // F31 ①: bound agents contribute their real SystemPrompt; unbound nodes keep the legacy
        // generic template so existing workflows behave identically (acceptance #5 backward compat).
        var systemPrompt = agent is not null
            ? agent.SystemPrompt
            : $"You are an agent executing the step \"{step.Name}\"." +
              " Produce a concise, actionable output relevant to this step.";

        var userParts = new List<string>
        {
            $"Execute workflow step: {step.Name} (order {step.Order})."
        };

        if (ctx.Artifacts.Count > 0)
        {
            var artifactLines = ctx.Artifacts.Values
                .Select(a => $"- {a.StepName}: {Truncate(a.Content, 300)}");
            userParts.Add("Previous step artifacts:\n" + string.Join("\n", artifactLines));
        }

        if (ctx.Blackboard.Entries.Count > 0)
        {
            // F36 D1=A：prompt 注入的 Blackboard 视图按 agent 软分区——绑定 agent 的步骤只见
            // 「全局共享区 + 自己分区（自分区键剥离前缀）」；未绑定 agent 的 LLM 步骤见全局区
            // （对既有工作流零变化：存量数据无 agent: 前缀键）。
            var boardEntries = agent is not null
                ? ctx.Blackboard.GetPartitionView(agent.Id)
                : ctx.Blackboard.GetGlobalView();
            if (boardEntries.Count > 0)
            {
                var boardLines = boardEntries
                    .Select(e => $"- {e.Key}: {Truncate(e.Value, 200)}");
                userParts.Add("Shared blackboard:\n" + string.Join("\n", boardLines));
            }
        }

        if (ctx.Summary.Summaries.Count > 0)        {
            // F33：压缩历史（含 [semantic-recall] 召回条目）真正进入 prompt
            var summaryLines = ctx.Summary.Summaries
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Value);
            userParts.Add("History summary:\n" + string.Join("\n", summaryLines));
        }

        if (ctx.Retrieval.HasContent)
        {
            // F33：RAG/语义召回片段注入
            var retrievalLines = ctx.Retrieval.Chunks
                .Select((chunk, i) => $"- ({i + 1}) {Truncate(chunk, 300)}");
            userParts.Add("Relevant knowledge:\n" + string.Join("\n", retrievalLines));
        }

        userParts.Add("Provide your output for this step.");

        return
        [
            new ChatMessage(MessageRole.System, systemPrompt),
            new ChatMessage(MessageRole.User, string.Join("\n\n", userParts))
        ];
    }

    private static string Truncate(string value, int maxLength) =>
        StringHelpers.Truncate(value, maxLength);

    /// <summary>
    /// F36 D2=A：把本轮 agent 调用持久化到 per-agent per-workflow 的会话——已存在则复用
    /// （同一工作流内同 agent 历史累积），不存在则创建（<see cref="Conversation.AgentId"/> 绑定）。
    /// 写入两条消息：user = 本轮 prompt 摘要，agent = 模型回复（含 token 用量）。
    /// 调用方已 best-effort 包裹：此处抛出的异常不会阻断工作流执行。
    /// </summary>
    private async Task PersistAgentConversationAsync(
        Agent agent,
        WorkflowContext ctx,
        string stepName,
        IReadOnlyList<ChatMessage> messages,
        string response,
        Domain.ValueObjects.TokenUsage? tokenUsage,
        CancellationToken ct)
    {
        var conversation = await _conversationRepository.GetByAgentAsync(ctx.TenantId, ctx.WorkflowId, agent.Id, ct);
        var created = conversation is null;
        if (created)
        {
            conversation = new Conversation(Guid.NewGuid(), ctx.TenantId, ctx.WorkflowId, agent.Id);
            _conversationRepository.Add(conversation);
            _logger.LogInformation(
                "Step {StepName}: created agent conversation {ConversationId} for agent {AgentId} (workflow {WorkflowId})",
                stepName, conversation.Id, agent.Id, ctx.WorkflowId);
        }

        // prompt 摘要：system prompt 之外的用户消息串联（与 prompt 注入同源，截断防超长）。
        var userContent = string.Join("\n\n", messages
            .Where(m => m.Role == MessageRole.User)
            .Select(m => m.Content));
        conversation!.AddMessage(new Message(Guid.NewGuid(), MessageRole.User, Truncate(userContent, 12000)));
        conversation.AddMessage(new Message(Guid.NewGuid(), MessageRole.Agent, Truncate(response, 12000), tokenUsage: tokenUsage));

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // F36 三道门修复：创建路径 SaveChanges 失败（典型=唯一过滤索引并发冲突）时，
            // 新实体仍以 Added 状态滞留在本 scope 共享的 change tracker 中——编排器紧随其后的
            // SaveChangesAsync（步骤状态落库）会重放同一冲突，把 best-effort 的「吞掉仅告警」
            // 放大成工作流状态保存失败。此处先行 Detach 隔离，保证 non-blocking 契约真正成立。
            if (created)
            {
                _conversationRepository.Detach(conversation);
            }

            throw;
        }
    }
}