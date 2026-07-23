namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 将文档文本切分为若干分块，供向量入库使用。
/// </summary>
public interface IDocumentChunker
{
    /// <summary>
    /// 将文本内容切分为有序分块。
    /// </summary>
    /// <param name="content">要切分的原始文本。</param>
    /// <returns>按出现顺序排列的分块列表（至少包含一个分块，空文本返回空列表）。</returns>
    IReadOnlyList<DocumentChunk> Chunk(string content);
}

/// <summary>
/// 文档切分后的单个分块。
/// </summary>
/// <param name="Content">分块文本内容。</param>
/// <param name="Index">分块在原文中的序号（从 0 开始）。</param>
public sealed record DocumentChunk(string Content, int Index);
