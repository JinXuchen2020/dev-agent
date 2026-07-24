namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// RAG 相关配置项（对应 appsettings 的 <c>Rag</c> 节）。
/// 放置于 Application 抽象层，供 Application 处理器与 Infrastructure 实现共同读取，
/// 避免 Application 反向依赖 Infrastructure。
/// </summary>
public sealed class RagSettings
{
    /// <summary>用于生成 embedding 的模型名称。</summary>
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>检索返回的默认最大结果数。</summary>
    public int DefaultTopK { get; set; } = 5;

    /// <summary>默认相关性阈值（余弦相似度）；低于此值的结果被过滤。</summary>
    public double DefaultMinScore { get; set; } = 0.7;

    /// <summary>文档切分的窗口大小（近似 token 数）。</summary>
    public int ChunkSizeTokens { get; set; } = 512;

    /// <summary>文档切分的重叠大小（近似 token 数）。</summary>
    public int ChunkOverlapTokens { get; set; } = 64;
}
