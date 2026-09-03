using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.ExecutionLogs.Commands.ReplayExecution;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.ExecutionLogs;

/// <summary>
/// F40 异常回放诊断单测：从执行日志条目只读重建路径、失败判定、数据缺口如实标注、
/// 跨租户不可读、检查点解析降级，以及「绝不写状态」这条硬约束。
/// </summary>
public sealed class ReplayExecutionCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();
    private readonly Guid _logId = Guid.NewGuid();

    private readonly IExecutionLogRepository _repository = Substitute.For<IExecutionLogRepository>();
    private readonly ITenantProvider _tenantProvider = Substitute.For<ITenantProvider>();

    public ReplayExecutionCommandHandlerTests() =>
        _tenantProvider.GetTenantId().Returns(TenantId);

    private static ExecutionLogEntry Entry(
        int order,
        string name,
        WorkflowState status,
        string? result = null,
        string? error = null,
        StepType? nodeType = StepType.LLM,
        int tokensIn = 10,
        int tokensOut = 5) =>
        new(Guid.NewGuid(), name, order, status, TimeSpan.FromMilliseconds(25), result, error,
            tokensIn, tokensOut, nodeType);

    private static ExecutionLog Log(Guid logId, WorkflowState status, params ExecutionLogEntry[] entries)
    {
        var log = new ExecutionLog(logId, WorkflowId, "failing-wf", TenantId, totalSteps: 3);
        foreach (var e in entries)
        {
            log.AddEntry(e);
        }

        if (status is WorkflowState.Completed)
        {
            log.Complete();
        }
        else if (status is WorkflowState.Failed)
        {
            log.Fail();
        }

        return log;
    }

    private ReplayExecutionCommandHandler Build() =>
        new(_repository, _tenantProvider);

    [Fact]
    public async Task Failed_Log_Rebuilds_Path_And_Marks_Failure_Node()
    {
        var log = Log(_logId, WorkflowState.Failed,
            Entry(0, "Start", WorkflowState.Completed, result: "kickoff"),
            Entry(1, "Generate", WorkflowState.Completed, result: "draft output"),
            Entry(2, "Review", WorkflowState.Failed, error: "模型返回超限"));
        _repository.GetByIdForTenantAsync(log.Id, TenantId, Arg.Any<CancellationToken>()).Returns(log);

        var report = await Build().Handle(new ReplayExecutionCommand(log.Id, TenantId), CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal(3, report!.Nodes.Count);
        Assert.Equal(WorkflowState.Failed, report.OverallStatus);
        Assert.Equal(1, report.FailurePath.FailedCount);
        Assert.Equal(2, report.FailurePath.FirstFailedStepOrder);
        Assert.Equal(["Review"], report.FailurePath.FailedStepNames);

        var failedNode = report.Nodes.Single(n => n.IsFailure);
        Assert.Equal("Review", failedNode.StepName);
        Assert.Contains("模型返回超限", failedNode.ErrorDetail);

        // 失败节点的前后上下文：输入为前序输出的推断值，必须显式标注。
        Assert.All(report.Nodes, n => Assert.True(n.InputInferred));
        Assert.Equal("draft output", failedNode.Input);
        Assert.Contains(ReplayDataGaps.NoInputSnapshot, report.DataGaps);
    }

    [Fact]
    public async Task Completed_Log_Has_No_Failure_Markers()
    {
        var log = Log(_logId, WorkflowState.Completed,
            Entry(0, "Start", WorkflowState.Completed, result: "a"),
            Entry(1, "Run", WorkflowState.Completed, result: "b"),
            Entry(2, "End", WorkflowState.Completed, result: "c"));
        _repository.GetByIdForTenantAsync(log.Id, TenantId, Arg.Any<CancellationToken>()).Returns(log);

        var report = await Build().Handle(new ReplayExecutionCommand(log.Id, TenantId), CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal(0, report!.FailurePath.FailedCount);
        Assert.Null(report.FailurePath.FirstFailedStepOrder);
        Assert.All(report.Nodes, n => Assert.False(n.IsFailure));
        Assert.Equal(3, report.RecordedStepCount);
        Assert.Equal(0, report.MissingStepCount);
    }

    [Fact]
    public async Task Unknown_Or_CrossTenant_Log_Returns_Null_For_404()
    {
        _repository.GetByIdForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ExecutionLog?)null);

        var report = await Build().Handle(
            new ReplayExecutionCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Null(report);
        // 归属校验发生在仓储层（带租户谓词），不是先读后比对的绕过写法。
        await _repository.Received(1).GetByIdForTenantAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task Legacy_Rows_Report_DataGaps_Instead_Of_Looking_Clean()
    {
        // F24 之前的旧行：无 NodeType、tokens 恒 0 → 必须如实标缺口，不能让前端把「无数据」当成「无问题」。
        var log = Log(_logId, WorkflowState.Completed,
            Entry(0, "Step A", WorkflowState.Completed, result: "x", nodeType: null, tokensIn: 0, tokensOut: 0));
        _repository.GetByIdForTenantAsync(log.Id, TenantId, Arg.Any<CancellationToken>()).Returns(log);

        var report = await Build().Handle(new ReplayExecutionCommand(log.Id, TenantId), CancellationToken.None);

        Assert.NotNull(report);
        Assert.Contains(ReplayDataGaps.LegacyNodeTypeMissing, report!.DataGaps);
        Assert.Contains(ReplayDataGaps.TokensNotReported, report.DataGaps);
        Assert.False(report.Nodes[0].TokensReported);
        Assert.Null(report.Nodes[0].NodeType);
    }

    [Fact]
    public async Task Truncated_Execution_Flags_Missing_Steps()
    {
        // 登记 5 步但只有 2 条日志（执行中断）→ 关键诊断信号，必须显式暴露。
        var log = new ExecutionLog(Guid.NewGuid(), WorkflowId, "cut-wf", TenantId, totalSteps: 5);
        log.AddEntry(Entry(0, "Start", WorkflowState.Completed, result: "a"));
        log.AddEntry(Entry(1, "Run", WorkflowState.Failed, error: "crash"));
        log.Fail();
        _repository.GetByIdForTenantAsync(log.Id, TenantId, Arg.Any<CancellationToken>()).Returns(log);

        var report = await Build().Handle(new ReplayExecutionCommand(log.Id, TenantId), CancellationToken.None);

        Assert.Equal(2, report!.RecordedStepCount);
        Assert.Equal(3, report.MissingStepCount);
        Assert.Contains(ReplayDataGaps.StepsMissing, report.DataGaps);
    }

    [Fact]
    public async Task Final_Checkpoint_Exposes_Context_Snapshot_With_Boundary_Note()
    {
        var log = Log(_logId, WorkflowState.Failed, Entry(0, "Run", WorkflowState.Failed, error: "x"));
        log.UpdateCheckpoint(
            """{"SchemaVersion":1,"CheckpointVersion":7,"Blackboard":{"loop.x":"1","trigger":"{}"},""" +
            """ "ExecutionOrderIndex":2,"LoopBodyIndices":{},"SkipSet":[],"StepStates":[],"TenantId":"t","WorkflowId":"w","CapturedAt":"2026-09-01T00:00:00Z"}""");
        _repository.GetByIdForTenantAsync(log.Id, TenantId, Arg.Any<CancellationToken>()).Returns(log);

        var report = await Build().Handle(new ReplayExecutionCommand(log.Id, TenantId), CancellationToken.None);

        var snapshot = report!.ContextSnapshot;
        Assert.True(snapshot.Available);
        Assert.Equal("F30-final-checkpoint", snapshot.Source);
        Assert.Equal("1", snapshot.Variables["loop.x"]);
        Assert.Equal(7, snapshot.CheckpointVersion);
        Assert.Equal(2, snapshot.ExecutionOrderIndex);
        // 必须声明这只是末次快照，不代表失败发生当时的上下文。
        Assert.Contains("末次", snapshot.Note);
    }

    [Fact]
    public async Task Corrupt_Checkpoint_Degrades_Without_Throwing()
    {
        var log = Log(_logId, WorkflowState.Failed, Entry(0, "Run", WorkflowState.Failed, error: "x"));
        log.UpdateCheckpoint("{ this is not json ");
        _repository.GetByIdForTenantAsync(log.Id, TenantId, Arg.Any<CancellationToken>()).Returns(log);

        var report = await Build().Handle(new ReplayExecutionCommand(log.Id, TenantId), CancellationToken.None);

        Assert.NotNull(report);
        Assert.False(report!.ContextSnapshot.Available);
        Assert.Contains(ReplayDataGaps.ContextSnapshotUnparsable, report.DataGaps);
    }

    [Fact]
    public async Task Missing_Checkpoint_Reports_Context_Unavailable()
    {
        var log = Log(_logId, WorkflowState.Failed, Entry(0, "Run", WorkflowState.Failed, error: "x"));
        _repository.GetByIdForTenantAsync(log.Id, TenantId, Arg.Any<CancellationToken>()).Returns(log);

        var report = await Build().Handle(new ReplayExecutionCommand(log.Id, TenantId), CancellationToken.None);

        Assert.False(report!.ContextSnapshot.Available);
        Assert.Contains(ReplayDataGaps.NoContextSnapshot, report.DataGaps);
    }

    [Fact]
    public async Task Long_Results_Are_Truncated_With_Length_Fidelity()
    {
        var big = new string('x', 9000);
        var log = Log(_logId, WorkflowState.Completed, Entry(0, "Run", WorkflowState.Completed, result: big));
        _repository.GetByIdForTenantAsync(log.Id, TenantId, Arg.Any<CancellationToken>()).Returns(log);

        var report = await Build().Handle(new ReplayExecutionCommand(log.Id, TenantId), CancellationToken.None);

        var node = report!.Nodes[0];
        Assert.Equal(4000, node.Output!.Length);
        Assert.True(node.OutputTruncated);
        Assert.Equal(9000, node.OutputLength);
    }

    [Fact]
    public async Task Truncate_Does_Not_Split_Surrogate_Pair()
    {
        // 边界正好落在代理对（增补平面 emoji）中间：3999 个 BMP 字符后跟一对代理，
        // 高位代理落在 index 3999（= MaxTextLength-1）。朴素切片会撕裂该码点，
        // 留下孤立高位代理 → 序列化成 U+FFFD 篡改文本。截断必须前退一位不撕裂码点。
        var emoji = "\U0001F600"; // 😀，UTF-16 需两个 code unit
        var big = new string('x', 3999) + emoji + new string('y', 10);
        var log = Log(_logId, WorkflowState.Completed, Entry(0, "Run", WorkflowState.Completed, result: big));
        _repository.GetByIdForTenantAsync(log.Id, TenantId, Arg.Any<CancellationToken>()).Returns(log);

        var report = await Build().Handle(new ReplayExecutionCommand(log.Id, TenantId), CancellationToken.None);

        var node = report!.Nodes[0];
        Assert.True(node.OutputTruncated);
        Assert.True(node.Output!.Length <= 4000);
        // 末位绝不能是孤立高位代理（代理对未被劈开）。
        Assert.False(char.IsHighSurrogate(node.Output[^1]));
        // 长度回传保留原始值，截断语义不受影响。
        Assert.Equal(big.Length, node.OutputLength);
    }

    [Fact]
    public async Task Replay_Is_ReadOnly_Never_Persists()
    {
        var log = Log(_logId, WorkflowState.Failed, Entry(0, "Run", WorkflowState.Failed, error: "x"));
        _repository.GetByIdForTenantAsync(log.Id, TenantId, Arg.Any<CancellationToken>()).Returns(log);

        await Build().Handle(new ReplayExecutionCommand(log.Id, TenantId), CancellationToken.None);

        // 只读诊断：不得改动聚合或落库（仓储写方法 Add/Update 零调用）。
        _repository.DidNotReceiveWithAnyArgs().Update(default!);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Guid_Empty_TenantId_Falls_Back_To_Ambient_Tenant_Not_To_Unfiltered_Read()
    {
        // 攻击兜底分支：未来调用方传 Guid.Empty 时，必须回落到 ambient 租户（fail-closed），
        // 绝不能退化成无租户过滤的读取。锁定该语义防止回归成 GetByIdAsync。
        var log = Log(_logId, WorkflowState.Completed, Entry(0, "Run", WorkflowState.Completed, result: "a"));
        _repository.GetByIdForTenantAsync(log.Id, TenantId, Arg.Any<CancellationToken>()).Returns(log);

        var report = await Build().Handle(new ReplayExecutionCommand(log.Id, Guid.Empty), CancellationToken.None);

        Assert.NotNull(report);
        await _repository.Received(1).GetByIdForTenantAsync(
            log.Id, TenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Node_List_Is_Capped_But_Failure_Stats_Cover_All_Entries()
    {
        // 循环展开等场景条目数无上界：响应必须封顶，但失败统计不得因截断而失真。
        var log = new ExecutionLog(Guid.NewGuid(), WorkflowId, "loop-wf", TenantId, totalSteps: 600);
        for (var i = 0; i < 599; i++)
        {
            log.AddEntry(Entry(i, $"Step {i}", WorkflowState.Completed, result: "ok"));
        }

        log.AddEntry(Entry(599, "Boom", WorkflowState.Failed, error: "late failure"));
        log.Fail();
        _repository.GetByIdForTenantAsync(log.Id, TenantId, Arg.Any<CancellationToken>()).Returns(log);

        var report = await Build().Handle(new ReplayExecutionCommand(log.Id, TenantId), CancellationToken.None);

        Assert.Equal(500, report!.Nodes.Count);
        Assert.Contains(ReplayDataGaps.NodesCapped, report.DataGaps);
        Assert.Equal(1, report.FailurePath.FailedCount);
        Assert.Equal(599, report.FailurePath.FirstFailedStepOrder);
        Assert.Equal(600, report.RecordedStepCount);
    }

    [Fact]
    public async Task Unregistered_TotalSteps_Is_Disclosed_Instead_Of_Looking_Complete()
    {
        // 生产路径 totalSteps 恒 0（WorkflowStartedEventHandler 建档）：此时 missingStepCount=0
        // 不代表尾部齐全，必须如实标缺口，避免「假健康」。
        var log = new ExecutionLog(Guid.NewGuid(), WorkflowId, "real-wf", TenantId, totalSteps: 0);
        log.AddEntry(Entry(0, "Run", WorkflowState.Completed, result: "a"));
        log.Complete();
        _repository.GetByIdForTenantAsync(log.Id, TenantId, Arg.Any<CancellationToken>()).Returns(log);

        var report = await Build().Handle(new ReplayExecutionCommand(log.Id, TenantId), CancellationToken.None);

        Assert.Equal(0, report!.MissingStepCount);
        Assert.Contains(ReplayDataGaps.TotalStepsUnregistered, report.DataGaps);
    }
}
