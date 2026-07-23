using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Services;

/// <summary>
/// 基于近似 token（空白分词）滑动窗口的文档切分器。
/// 按配置的窗口大小切分，并以重叠大小回退，保证上下文连续性。
/// 此为近似实现（以空白词数近似 token 数），满足地基层需求；生产可替换为真实 tokenizer。
/// </summary>
internal sealed class WordWindowChunker : IDocumentChunker
{
    private readonly RagSettings _settings;

    /// <summary>初始化 <see cref="WordWindowChunker"/> 的新实例。</summary>
    public WordWindowChunker(IOptions<RagSettings> options) => _settings = options.Value;

    /// <inheritdoc/>
    public IReadOnlyList<DocumentChunk> Chunk(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new List<DocumentChunk>();

        var tokens = content.Split([' ', '\t', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return new List<DocumentChunk> { new(content.Trim(), 0) };

        var window = Math.Max(1, _settings.ChunkSizeTokens);
        var overlap = Math.Clamp(_settings.ChunkOverlapTokens, 0, window - 1);
        var step = window - overlap;

        var chunks = new List<DocumentChunk>();
        var index = 0;
        for (var start = 0; start < tokens.Length; start += step)
        {
            var end = Math.Min(start + window, tokens.Length);
            var piece = string.Join(' ', tokens[start..end]);
            chunks.Add(new DocumentChunk(piece, index++));
            if (end >= tokens.Length)
                break;
        }

        return chunks;
    }
}
