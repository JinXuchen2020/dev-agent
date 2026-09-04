using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.ExecutionLogs.Commands.ReplayExecution;

/// <summary>回放报告中单个节点的重建视图（只读，源自执行日志条目）。</summary>
/// <param name="StepOrder">步骤序号（重建路径的排序键）。</param>
/// <param name="StepName">步骤名。</param>
/// <param name="Status">该步骤终态（原样回传，供前端分色）。</param>
/// <param name="NodeType">节点类型；<c>null</c> 表示 F24 埋点之前的旧数据（见报告 dataGaps）。</param>
/// <param name="IsFailure">失败判定：<c>Status==Failed</c> 或存在 <c>ErrorDetail</c>。</param>
/// <param name="StartedAt">开始时间。</param>
/// <param name="CompletedAt">结束时间。</param>
/// <param name="DurationMs">耗时（毫秒）。</param>
/// <param name="Input">推断输入 = 前序节点（截断后）的输出；首节点无推断来源为 null。平台未记录真实入参，见 <paramref name="InputInferred"/>。</param>
/// <param name="InputInferred">true = 该输入为推断值而非落库快照（诚实标注，避免误读为真实入参）。</param>
/// <param name="Output">节点输出（截断后）。</param>
/// <param name="OutputLength">输出原始长度。</param>
/// <param name="OutputTruncated">输出是否被截断。</param>
/// <param name="ErrorDetail">错误详情（截断后），成功为 null。</param>
/// <param name="ErrorTruncated">错误详情是否被截断。</param>
/// <param name="TokensIn">入参 token（0 且 <paramref name="TokensReported"/>=false 时视为未上报）。</param>
/// <param name="TokensOut">出参 token。</param>
/// <param name="TokensReported">该节点是否有 token 上报（旧数据/非模型节点为 false）。</param>
public sealed record ReplayNodeView(
    int StepOrder,
    string StepName,
    WorkflowState Status,
    StepType? NodeType,
    bool IsFailure,
    DateTime StartedAt,
    DateTime? CompletedAt,
    long DurationMs,
    string? Input,
    bool InputInferred,
    string? Output,
    int OutputLength,
    bool OutputTruncated,
    string? ErrorDetail,
    bool ErrorTruncated,
    int TokensIn,
    int TokensOut,
    bool TokensReported);

/// <summary>失败路径摘要。</summary>
/// <param name="FirstFailedStepOrder">首个失败节点序号；无失败为 null。</param>
/// <param name="FailedStepNames">失败节点名（按序号）。</param>
/// <param name="FailedCount">失败节点数。</param>
public sealed record ReplayFailurePath(int? FirstFailedStepOrder, IReadOnlyList<string> FailedStepNames, int FailedCount);

/// <summary>
/// 上下文快照。<b>能力边界</b>：F30 检查点只保留<b>末次</b>快照（覆盖写），故无法重建每一步的
/// Blackboard 历史 —— 此处只提供末次快照，并在 <see cref="Note"/> 明示，绝不声称 per-step 可回放。
/// </summary>
/// <param name="Available">是否有可用快照。</param>
/// <param name="Source">快照来源标识。</param>
/// <param name="Variables">Blackboard 键值（已尽力解析）。</param>
/// <param name="CheckpointVersion">检查点版本。</param>
/// <param name="ExecutionOrderIndex">检查点记录的执行序号游标。</param>
/// <param name="StepStateCount">检查点内步骤状态条数（可用于核对覆盖范围）。</param>
/// <param name="Note">边界/降级说明。</param>
public sealed record ReplayContextSnapshot(
    bool Available,
    string? Source,
    IReadOnlyDictionary<string, string> Variables,
    int? CheckpointVersion,
    int? ExecutionOrderIndex,
    int StepStateCount,
    string Note);

/// <summary>F40 回放报告：从执行日志重建的异常路径诊断。</summary>
/// <param name="ExecutionLogId">来源日志。</param>
/// <param name="WorkflowId">工作流。</param>
/// <param name="WorkflowName">工作流名。</param>
/// <param name="OverallStatus">执行终态。</param>
/// <param name="StartedAt">开始时间。</param>
/// <param name="CompletedAt">结束时间。</param>
/// <param name="TotalSteps">登记的总步骤数（与实际落条目数可能不等）。</param>
/// <param name="Nodes">按序号重建的节点路径（超上限时截断，并在 <paramref name="DataGaps"/> 标注 report-nodes-capped）。</param>
/// <param name="FailurePath">失败路径摘要。</param>
/// <param name="ContextSnapshot">末次上下文快照（含能力边界说明）。</param>
/// <param name="RecordedStepCount">实际有日志条目的步骤数。</param>
/// <param name="MissingStepCount">缺失条目的步骤数（TotalSteps - RecordedStepCount，负数归零）——异常中断时的关键信号；
/// 生产建档的 TotalSteps 恒为 0，此时本值恒 0 不代表尾部齐全，须结合 dataGaps 的 total-steps-unregistered 判读。</param>
/// <param name="DataGaps">数据缺口码列表（前端据此灰显并提示，避免把「信息缺失」读成「没有失败」）。</param>
public sealed record ReplayReport(
    Guid ExecutionLogId,
    Guid WorkflowId,
    string WorkflowName,
    WorkflowState OverallStatus,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int TotalSteps,
    IReadOnlyList<ReplayNodeView> Nodes,
    ReplayFailurePath FailurePath,
    ReplayContextSnapshot ContextSnapshot,
    int RecordedStepCount,
    int MissingStepCount,
    IReadOnlyList<string> DataGaps);

/// <summary>
/// F40 异常回放诊断：从 <see cref="ExecutionLog"/> 条目<b>只读重建</b>失败工作流的执行路径
/// （不重新执行任何步骤、不写任何状态）。复用 F24 Trace 字段与 F30 检查点数据。
/// </summary>
/// <param name="ExecutionLogId">目标执行日志。</param>
/// <param name="TenantId">调用租户（<see cref="ExecutionLog"/> 不实现 ITenantScoped，仓储按 id 直读不过滤租户，故此处显式校归属）。</param>
/// <remarks>
/// 纯只读诊断，故用 MediatR <see cref="IRequest{TResponse}"/> 而非
/// <c>ICommand&lt;T&gt;</c>——后者会经 UnitOfWorkBehavior 触发一次 SaveChanges，
/// 与「回放不写任何状态」的承诺相悖（也让审查者误以为存在副作用）。
/// </remarks>
public sealed record ReplayExecutionCommand(Guid ExecutionLogId, Guid TenantId)
    : IRequest<ReplayReport?>;

/// <summary>数据缺口码（稳定字面量，供前端与指南对照）。</summary>
internal static class ReplayDataGaps
{
    /// <summary>平台未记录每节点真实入参，输入为推断值。</summary>
    public const string NoInputSnapshot = "input-snapshot-unavailable";

    /// <summary>F24 之前的旧行无 NodeType（线性步骤），节点类型不可判别。</summary>
    public const string LegacyNodeTypeMissing = "node-type-missing-legacy-rows";

    /// <summary>存在 tokens 全为 0 的节点（旧数据或非模型节点），成本不可判。</summary>
    public const string TokensNotReported = "tokens-not-reported";

    /// <summary>无可用上下文快照（从未落检查点或数据缺失）。</summary>
    public const string NoContextSnapshot = "context-snapshot-unavailable";

    /// <summary>检查点 JSON 无法解析（数据损坏/格式演进），已降级。</summary>
    public const string ContextSnapshotUnparsable = "context-snapshot-unparsable";

    /// <summary>日志条目数少于登记步骤数（执行被中断，尾部步骤无条目）。</summary>
    public const string StepsMissing = "steps-missing-truncated-execution";

    /// <summary>聚合未登记步骤总数（生产路径 TotalSteps=0），缺失步数不可判。</summary>
    public const string TotalStepsUnregistered = "total-steps-unregistered";

    /// <summary>节点列表超上限被截断（仅呈现前 MaxNodesInReport 个节点；失败统计仍基于全量条目）。</summary>
    public const string NodesCapped = "report-nodes-capped";
}

internal sealed class ReplayExecutionCommandHandler(
    IExecutionLogRepository repository,
    ITenantProvider tenantProvider)
    : IRequestHandler<ReplayExecutionCommand, ReplayReport?>
{
    /// <summary>长文本截断上限：诊断端点不应拖出 MB 级响应；全文走既有详情端点。</summary>
    private const int MaxTextLength = 4000;

    /// <summary>报告节点数上限：循环展开可使条目数无上界，响应体积必须封顶（失败统计仍基于全量条目）。</summary>
    private const int MaxNodesInReport = 500;

    /// <summary>失败步名列表上限：同名失败（每轮循环一条）不得撑爆 payload。</summary>
    private const int MaxFailedStepNames = 50;

    public async Task<ReplayReport?> Handle(ReplayExecutionCommand request, CancellationToken ct)
    {
        var tenantId = request.TenantId == Guid.Empty ? tenantProvider.GetTenantId() : request.TenantId;
        var log = await repository.GetByIdForTenantAsync(request.ExecutionLogId, tenantId, ct);
        if (log is null)
        {
            return null; // 不存在或跨租户 → 404（不暴露存在性）
        }

        var ordered = log.Entries
            .OrderBy(e => e.StepOrder)
            .ThenBy(e => e.StartedAt)
            .ToList();

        var gaps = new List<string>();
        var nodes = new List<ReplayNodeView>(ordered.Count);

        string? previousOutput = null;
        var legacyNodeType = false;
        var tokensAbsent = false;

        for (var i = 0; i < ordered.Count; i++)
        {
            var entry = ordered[i];
            var isFailure = entry.Status is WorkflowState.Failed || !string.IsNullOrEmpty(entry.ErrorDetail);
            if (entry.NodeType is null)
            {
                legacyNodeType = true;
            }

            if (entry.TokensIn == 0 && entry.TokensOut == 0)
            {
                tokensAbsent = true;
            }

            var (output, outputTruncated, outputLength) = Truncate(entry.Result);
            var (errorDetail, errorTruncated, _) = Truncate(entry.ErrorDetail);

            // 推断输入：其余节点用前序输出；首节点不可得——执行上下文属 Workflow 聚合的
            // **当前值**（非当时快照），日志未落该字段，故如实返回 null 而非拿现在的值冒充历史。
            string? input = i == 0 ? null : previousOutput;

            nodes.Add(new ReplayNodeView(
                entry.StepOrder,
                entry.StepName,
                entry.Status,
                entry.NodeType,
                isFailure,
                entry.StartedAt,
                entry.CompletedAt,
                (long)entry.Duration.TotalMilliseconds,
                input,
                InputInferred: true,
                output,
                outputLength,
                outputTruncated,
                errorDetail,
                errorTruncated,
                entry.TokensIn,
                entry.TokensOut,
                TokensReported: entry.TokensIn > 0 || entry.TokensOut > 0));

            if (output is not null)
            {
                previousOutput = output;
            }
        }

        if (legacyNodeType)
        {
            gaps.Add(ReplayDataGaps.LegacyNodeTypeMissing);
        }

        if (tokensAbsent)
        {
            gaps.Add(ReplayDataGaps.TokensNotReported);
        }

        // 平台从不落库每节点真实入参 —— 始终如实标注，避免前端把推断值当真实输入。
        gaps.Add(ReplayDataGaps.NoInputSnapshot);

        var missingSteps = Math.Max(0, log.TotalSteps - ordered.Count);
        if (missingSteps > 0)
        {
            gaps.Add(ReplayDataGaps.StepsMissing);
        }

        // 生产路径的工作流启动事件以 totalSteps:0 建档且聚合不可变该字段 —— 此时
        // missingStepCount 恒 0 并不代表「尾部齐全」，必须如实标注不可判，避免假健康。
        if (log.TotalSteps <= 0)
        {
            gaps.Add(ReplayDataGaps.TotalStepsUnregistered);
        }

        var failed = nodes.Where(n => n.IsFailure).ToList();
        var failurePath = new ReplayFailurePath(
            failed.Count > 0 ? failed[0].StepOrder : null,
            failed.Take(MaxFailedStepNames).Select(n => n.StepName).ToList(),
            failed.Count);

        // 响应体积封顶：循环展开的日志条目可远超节点上限；时间线只呈现前 MaxNodesInReport 个节点，
        // 失败统计（failedCount/firstFailedStepOrder）仍基于全量条目，缺口码显式披露截断。
        var reportedNodes = nodes.Count > MaxNodesInReport
            ? nodes.Take(MaxNodesInReport).ToList()
            : nodes;
        if (nodes.Count > MaxNodesInReport)
        {
            gaps.Add(ReplayDataGaps.NodesCapped);
        }

        var snapshot = BuildContextSnapshot(log, gaps);

        return new ReplayReport(
            log.Id,
            log.WorkflowId,
            log.WorkflowName,
            log.Status,
            log.StartedAt,
            log.CompletedAt,
            log.TotalSteps,
            reportedNodes,
            failurePath,
            snapshot,
            ordered.Count,
            missingSteps,
            gaps);
    }

    /// <summary>
    /// 解析 F30 末次检查点的 Blackboard 快照。检查点结构为 Infrastructure 私有类型，
    /// 故用 JsonDocument 宽松解析（兼容 PascalCase/camelCase 与字段缺失），任何异常一律降级为
    /// 不可用并记数据缺口，绝不让诊断端点因脏数据 500。
    /// </summary>
    private static ReplayContextSnapshot BuildContextSnapshot(ExecutionLog log, List<string> gaps)
    {
        if (string.IsNullOrWhiteSpace(log.CheckpointData))
        {
            gaps.Add(ReplayDataGaps.NoContextSnapshot);
            return new ReplayContextSnapshot(
                false, null, new Dictionary<string, string>(), null, null, 0,
                "无检查点数据（执行未进入可检查点阶段，或为 F30 之前的历史日志）。");
        }

        try
        {
            using var doc = JsonDocument.Parse(log.CheckpointData);
            var root = doc.RootElement;

            var variables = new Dictionary<string, string>();
            if (TryGetPropertyCaseInsensitive(root, "Blackboard", out var board) && board.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in board.EnumerateObject())
                {
                    variables[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? string.Empty
                        : prop.Value.GetRawText();
                }
            }

            int? checkpointVersion = TryGetInt(root, "CheckpointVersion", out var cv) ? cv : null;
            int? orderIndex = TryGetInt(root, "ExecutionOrderIndex", out var oi) ? oi : null;
            var stepStateCount = TryGetPropertyCaseInsensitive(root, "StepStates", out var states) && states.ValueKind == JsonValueKind.Array
                ? states.GetArrayLength()
                : 0;

            return new ReplayContextSnapshot(
                true,
                "F30-final-checkpoint",
                variables,
                checkpointVersion ?? log.CheckpointVersion,
                orderIndex,
                stepStateCount,
                "末次检查点快照（F30 覆盖写，非 per-step 历史）；仅表示执行中断/结束时的上下文，不代表失败发生当时的上下文。");
        }
        catch (JsonException)
        {
            gaps.Add(ReplayDataGaps.ContextSnapshotUnparsable);
            return new ReplayContextSnapshot(
                false, "F30-final-checkpoint", new Dictionary<string, string>(),
                log.CheckpointVersion, null, 0,
                "检查点 JSON 无法解析（数据损坏或格式演进），已降级为不展示上下文。");
        }
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        value = 0;
        if (!TryGetPropertyCaseInsensitive(root, name, out var el) || el.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        return el.TryGetInt32(out value);
    }

    private static (string? Text, bool Truncated, int Length) Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (null, false, 0);
        }

        if (text.Length <= MaxTextLength)
        {
            return (text, false, text.Length);
        }

        // 截断按 UTF-16 code unit 计数，若边界恰落在代理对（emoji / 增补平面汉字）中间会劈开
        // 一对代理，留下孤立高位代理 → 序列化成替换字符（U+FFFD），静默篡改诊断文本。故当末位
        // 为高位代理时前退一位，保证截断边界不撕裂码点；OutputLength 仍回传原始长度（截断语义不变）。
        var cut = MaxTextLength;
        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return (text[..cut], true, text.Length);
    }
}
