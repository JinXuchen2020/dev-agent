using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Evaluation;
using AgentPlatform.Application.Evaluation.Commands.RunEvaluation;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Application.Evaluation.Commands.RunEvaluationGate;

/// <summary>
/// F34 在线评估门禁：对目标工作流跑数据集回归，并按通过率阈值给出**阻断语义**的判定。
/// 阈值解析链：请求显式值 > EvaluationSettings.GateMinPassRate（默认 0.8）。
/// 执行复用 RunEvaluationCommandHandler（一次性克隆 = 影子隔离，零生产写入）。
/// </summary>
public sealed record RunEvaluationGateCommand(
    Guid DatasetId,
    Guid WorkflowId,
    double? MinPassRate) : ICommand<EvaluationGateResult>;

/// <summary>门禁判定结果：<see cref="Passed"/>=false 时调用方（CI/发布流水线）必须阻断。</summary>
public sealed record EvaluationGateResult(
    bool Passed,
    double MinPassRate,
    int Total,
    int PassedCases,
    double Score,
    EvaluationReport Report);

internal sealed class RunEvaluationGateCommandHandler(
    IMediator mediator,
    IAuditLogRepository auditLogRepository,
    ITenantProvider tenantProvider,
    IOptions<EvaluationSettings> evalSettings,
    ILogger<RunEvaluationGateCommandHandler> logger)
    : IRequestHandler<RunEvaluationGateCommand, EvaluationGateResult>
{
    public async Task<EvaluationGateResult> Handle(RunEvaluationGateCommand request, CancellationToken ct)
    {
        var minPassRate = request.MinPassRate ?? evalSettings.Value.GateMinPassRate;
        if (minPassRate is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(request), "minPassRate must be within [0, 1].");

        var report = await mediator.Send(new RunEvaluationCommand(request.DatasetId, request.WorkflowId), ct);

        // 空数据集 Score=0 → 不通过：防「无数据即放行」漏洞（显式守卫，min=0 也拦）
        var passed = report.Total > 0 && report.Score >= minPassRate;

        var tenantId = tenantProvider.GetTenantId();
        auditLogRepository.Add(AuditLog.Record(
            tenantId,
            AuditActionType.EvaluationGate,
            "EvaluationDataset",
            entityId: request.DatasetId,
            details: $"Gate workflow {request.WorkflowId}: score {report.Score:P1} vs threshold {minPassRate:P0} => {(passed ? "PASS" : "BLOCK")}"));
        // 审计落库由 UnitOfWorkBehavior 在命令管线统一提交

        logger.LogInformation(
            "Evaluation gate dataset {DatasetId} vs workflow {WorkflowId}: {Passed}/{Total} (score {Score:P1}, threshold {Threshold:P0}) -> {Verdict}",
            request.DatasetId, request.WorkflowId, report.Passed, report.Total,
            report.Score, minPassRate, passed ? "PASS" : "BLOCK");

        return new EvaluationGateResult(passed, minPassRate, report.Total, report.Passed, report.Score, report);
    }
}