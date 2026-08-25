namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 语义记忆设置（F33）。控制 episodic 写回开关与召回参数。
/// </summary>
public sealed class SemanticMemorySettings
{
    /// <summary>是否启用语义记忆（写回与召回总开关）。默认 true。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Compaction 时语义召回的最大条数。默认 3。</summary>
    public int RecallTopK { get; set; } = 3;

    /// <summary>召回相关性阈值（余弦相似度下限）。默认 0.6。</summary>
    public double RecallMinScore { get; set; } = 0.6;
}