using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Services;

/// <summary>
/// 覆盖 IDocumentChunker（R1 入库切分）：窗口 + 重叠滑动切分行为。
/// </summary>
public sealed class WordWindowChunkerTests
{
    private static WordWindowChunker Create(int window, int overlap) =>
        new(Options.Create(new RagSettings
        {
            ChunkSizeTokens = window,
            ChunkOverlapTokens = overlap
        }));

    [Fact]
    public void EmptyContent_ReturnsEmptyList()
    {
        var chunker = Create(512, 64);

        var chunks = chunker.Chunk("   ");

        Assert.Empty(chunks);
    }

    [Fact]
    public void ShortContent_SingleChunkIndexZero()
    {
        var chunker = Create(512, 64);

        var chunks = chunker.Chunk("just a short sentence");

        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].Index);
        Assert.Equal("just a short sentence", chunks[0].Content);
    }

    [Fact]
    public void LongContent_SplitsByWindowWithOverlap()
    {
        var chunker = Create(4, 2);
        var words = string.Join(' ', Enumerable.Range(0, 12).Select(i => $"w{i}"));

        var chunks = chunker.Chunk(words);

        Assert.Equal(5, chunks.Count); // 12 words, window 4, step 2 -> ceil((12-4)/2)+1 = 5
        for (var i = 0; i < chunks.Count; i++)
            Assert.Equal(i, chunks[i].Index);

        // 重叠区：chunk1 的前 overlap(2) 个词应与 chunk0 的后 2 个词一致（w2 w3）
        Assert.EndsWith("w2 w3", chunks[0].Content);
        Assert.StartsWith("w2 w3", chunks[1].Content);
    }

    [Fact]
    public void OverlapClamped_WhenExceedsWindow()
    {
        var chunker = Create(4, 99); // overlap > window-1 应被夹到 3
        var words = string.Join(' ', Enumerable.Range(0, 8).Select(i => $"w{i}"));

        var chunks = chunker.Chunk(words);

        // step = window - clampedOverlap = 4 - 3 = 1 -> 每步前进 1
        Assert.Equal(5, chunks.Count); // (8-4)/1 + 1 = 5
    }
}
