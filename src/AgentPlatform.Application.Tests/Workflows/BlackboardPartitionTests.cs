using AgentPlatform.Application.Abstractions;
using Xunit;

namespace AgentPlatform.Application.Tests.Workflows;

/// <summary>
/// F36 D1=A（软分区视图）Blackboard 单测：
/// · agent 分区键 <c>agent:{agentId}:{key}</c> 只在所属 agent 的分区视图可见（自分区键剥离前缀）。
/// · 全局共享区（无 agent: 前缀）对所有视图可见。
/// · GetGlobalView 剔除全部 agent 分区键（未绑定 agent 的 LLM 步骤语义，对存量数据零变化）。
/// · 持久化格式不变：Entries 仍是扁平 string→string（F30 检查点 / F25 调试器 / RunningExecution 快照零迁移）。
/// </summary>
public sealed class BlackboardPartitionTests
{
    [Fact]
    public void SetInPartition_RoundTrips_Via_GetFromPartition()
    {
        var agentId = Guid.NewGuid();
        var board = Blackboard.Empty;

        board.SetInPartition(agentId, "plan", "先检索再写");

        Assert.Equal("先检索再写", board.GetFromPartition(agentId, "plan"));
        Assert.Null(board.GetFromPartition(Guid.NewGuid(), "plan"));
    }

    [Fact]
    public void PartitionView_Contains_Global_And_Own_Only()
    {
        var agentA = Guid.NewGuid();
        var agentB = Guid.NewGuid();
        var board = Blackboard.Empty
            .Set("shared", "全局值")
            .SetInPartition(agentA, "plan", "A 的计划")
            .SetInPartition(agentB, "plan", "B 的计划");

        var viewA = board.GetPartitionView(agentA);

        Assert.Equal(2, viewA.Count);
        Assert.Equal("全局值", viewA["shared"]);
        Assert.Equal("A 的计划", viewA["plan"]); // 自分区键剥离前缀
        // B 的分区内容绝不出现
        Assert.DoesNotContain("B 的计划", viewA.Values);
    }

    [Fact]
    public void PartitionView_Is_ReadOnly_Snapshot_Does_Not_Expose_Internal_Storage()
    {
        var agentId = Guid.NewGuid();
        var board = Blackboard.Empty.SetInPartition(agentId, "k", "v");

        var view = board.GetPartitionView(agentId);
        board.SetInPartition(agentId, "k", "v2");

        // 视图是快照：底层的后续写入不回溯修改已取出的视图
        Assert.Equal("v", view["k"]);
        Assert.Equal("v2", board.GetFromPartition(agentId, "k"));
    }

    [Fact]
    public void GlobalView_Excludes_All_Agent_Partition_Keys()
    {
        var agentA = Guid.NewGuid();
        var board = Blackboard.Empty
            .Set("loop.x", "1")
            .Set("trigger", "{}")
            .SetInPartition(agentA, "plan", "A 的计划")
            .Set(Blackboard.AgentOutputKey(agentA), "A 的输出");

        var global = board.GetGlobalView();

        Assert.Equal(2, global.Count);
        Assert.True(global.ContainsKey("loop.x"));
        Assert.True(global.ContainsKey("trigger"));
        Assert.DoesNotContain(global.Keys, k => k.StartsWith(Blackboard.AgentKeyPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void Entries_Remains_Flat_Store_For_Persistence_Compat()
    {
        // 持久化兼容（D1=A 核心）：F30 检查点 / F25 调试器 / RunningExecution 快照直接序列化
        // Entries——分区键以原样扁平键落盘，格式零变更。
        var agentId = Guid.NewGuid();
        var board = Blackboard.Empty
            .Set("shared", "v")
            .SetInPartition(agentId, "plan", "p");

        Assert.Equal(2, board.Entries.Count);
        Assert.Equal("v", board.Entries["shared"]);
        Assert.Equal("p", board.Entries[Blackboard.PartitionKey(agentId, "plan")]);
    }

    [Fact]
    public void AgentOutputKey_Is_Stable_Contract_Key()
    {
        var agentId = Guid.NewGuid();
        Assert.Equal($"agent:{agentId}:output", Blackboard.AgentOutputKey(agentId));
    }

    [Fact]
    public void Empty_Board_Partition_And_Global_Views_Are_Empty()
    {
        var board = Blackboard.Empty;

        Assert.Empty(board.GetPartitionView(Guid.NewGuid()));
        Assert.Empty(board.GetGlobalView());
    }
}
