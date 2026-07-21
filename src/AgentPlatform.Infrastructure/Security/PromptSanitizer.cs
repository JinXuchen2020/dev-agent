using System.Text.RegularExpressions;
using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Infrastructure.Security;

/// <summary>
/// Detects and blocks prompt injection patterns in user inputs.
/// </summary>
internal sealed class PromptSanitizer : IPromptSanitizer
{
    private static readonly string[] InjectionPatterns =
    [
        @"ignore\s+(previous|all|any)\s+instructions",
        @"forget\s+(all|previous|any)\s+instructions",
        @"you\s+are\s+now",
        @"from\s+now\s+on\s+you\s+are",
        @"act\s+as\s+(a|the|an)",
        @"system\s*:",
        @"SYSTEM\s*PROMPT\s*:",
        @"DO\s+NOT\s+FOLLOW\s+INSTRUCTIONS",
        @"output\s+(your|the)\s+system\s+prompt",
        @"reveal\s+(your|the)\s+instructions",
        @"ignore\s+the\s+above",
        @"disregard\s+(previous|all)\s+prompts"
    ];

    private static readonly Regex[] s_patterns = InjectionPatterns
        .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
        .ToArray();

    /// <summary>
    /// Sanitizes a user message. Returns null if blocked as injection.
    /// Truncates messages exceeding MaxLength.
    /// </summary>
    public string? Sanitize(string userInput)
    {
        ArgumentNullException.ThrowIfNull(userInput);

        // Check for injection patterns
        foreach (var pattern in s_patterns)
        {
            if (pattern.IsMatch(userInput))
                return null; // Block potential injection
        }

        // Truncate excessively long messages
        const int maxLength = 10000;
        if (userInput.Length > maxLength)
            return userInput[..maxLength];

        return userInput;
    }

    /// <summary>
    /// Wraps a system prompt with clear delimiters to prevent boundary confusion.
    /// Format: ```xml\n{systemPrompt}\n```
    /// </summary>
    public string WrapSystemPrompt(string systemPrompt)
    {
        ArgumentNullException.ThrowIfNull(systemPrompt);
        return $"```xml\n{systemPrompt}\n```";
    }
}
