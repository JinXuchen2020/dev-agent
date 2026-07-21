namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides prompt injection detection and sanitization for user inputs.
/// </summary>
public interface IPromptSanitizer
{
    /// <summary>
    /// Sanitizes a user message to prevent prompt injection attacks.
    /// Returns null if the input is blocked as a potential injection.
    /// </summary>
    string? Sanitize(string userInput);

    /// <summary>
    /// Wraps a system prompt with clear delimiters to prevent boundary confusion.
    /// </summary>
    string WrapSystemPrompt(string systemPrompt);
}
