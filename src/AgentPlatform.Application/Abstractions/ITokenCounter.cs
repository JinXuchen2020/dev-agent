namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Estimates token counts for text, used to prevent context-window overflow
/// in orchestration summary history (Blueprint C.3.1 / F5).
/// Implementations may use character-based heuristics, a BPE tokenizer, or an ML model.
/// </summary>
public interface ITokenCounter
{
    /// <summary>
    /// Returns an estimated token count for the given text.
    /// Accuracy depends on the implementation; the contract guarantees
    /// a monotonic relationship (more text → higher count) suitable
    /// for budget-based truncation decisions.
    /// </summary>
    int CountTokens(string text);
}
