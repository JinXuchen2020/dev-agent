using AgentPlatform.Infrastructure.Tokenizers;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Tokenizers;

public sealed class TokenCounterTests
{
    private readonly TokenCounter _counter = new();

    [Fact]
    public void CountTokens_ReturnsPositive_ForNonEmptyText()
    {
        var count = _counter.CountTokens("Hello, world! This is a test of the token counter.");

        Assert.True(count > 0);
    }

    [Fact]
    public void CountTokens_ReturnsZero_ForEmptyString()
    {
        var count = _counter.CountTokens(string.Empty);

        Assert.Equal(0, count);
    }

    [Fact]
    public void CountTokens_ReturnsZero_ForNullString()
    {
        var count = _counter.CountTokens(null!);

        Assert.Equal(0, count);
    }

    [Fact]
    public void CountTokens_DoesNotThrow_OnVeryLongText()
    {
        // Generate a 100 KB string of ASCII text
        var longText = new string('A', 100_000);

        var exception = Record.Exception(() => _counter.CountTokens(longText));

        Assert.Null(exception);
    }

    [Fact]
    public void CountTokens_ReturnsHigherCount_ForLongerText()
    {
        var shortText = "Hello";
        var longText = "Hello, this is a much longer piece of text that should cost more tokens.";

        var shortCount = _counter.CountTokens(shortText);
        var longCount = _counter.CountTokens(longText);

        Assert.True(longCount > shortCount);
    }

    [Fact]
    public void CountTokens_CjkText_ReturnsPositiveCount()
    {
        var cjkText = "你好世界";
        var count = _counter.CountTokens(cjkText);

        Assert.True(count > 0);
    }
}
