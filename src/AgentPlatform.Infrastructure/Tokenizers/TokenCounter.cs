using System.Runtime.CompilerServices;
using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Infrastructure.Tokenizers;

/// <summary>
/// Lightweight language-aware token counter that estimates token counts
/// using character-category heuristics (no external tokenizer model required).
///
/// Heuristics (based on observed GPT-4o / Claude tokenizer behavior):
///   - ASCII / Latin-1 (U+0000–U+007F):           1 token ≈ 4 chars
///   - CJK / fullwidth (U+3000–U+9FFF, U+FF00–U+FFEF): 1 token ≈ 1.5 chars
///   - Everything else (emoji, accented, etc.):    1 token ≈ 2 chars
///
/// These ratios are conservative (slightly over-estimate) to ensure
/// the context-window safety margin is not breached.
/// </summary>
internal sealed class TokenCounter : ITokenCounter
{
    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Pre-allocate with a reasonable initial capacity.
        var tokens = 0.0;

        // Fast path: scan char by char without allocating substrings.
        // ReSharper disable once ForCanBeConvertedToForeach — performance; no alloc.
        // ReSharper disable once LoopCanBeConvertedToQuery — readability trade-off.
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c <= 0x7f)
            {
                // ASCII — 1 token per 4 chars
                tokens += 0.25;
            }
            else if (IsCjk(c))
            {
                // CJK / fullwidth — 1 token per 1.5 chars
                tokens += 2.0 / 3.0;
            }
            else
            {
                // Other (emoji, accented, etc.) — 1 token per 2 chars
                tokens += 0.5;
            }
        }

        // Round up so we never under-estimate (conservative / safety-first).
        return (int)Math.Ceiling(tokens);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCjk(char c)
    {
        // CJK Unified Ideographs
        if (c >= 0x4e00 && c <= 0x9fff) return true;
        // CJK Symbols and Punctuation
        if (c >= 0x3000 && c <= 0x303f) return true;
        // Fullwidth Forms (FF00–FFEF) — includes fullwidth ASCII variants
        if (c >= 0xff00 && c <= 0xffef) return true;
        // CJK Unified Ideographs Extension A
        if (c >= 0x3400 && c <= 0x4dbf) return true;
        // CJK Compatibility Ideographs
        if (c >= 0xf900 && c <= 0xfaff) return true;
        // CJK Radicals Supplement
        if (c >= 0x2e80 && c <= 0x2eff) return true;
        // Kangxi Radicals
        if (c >= 0x2f00 && c <= 0x2fdf) return true;

        return false;
    }
}
