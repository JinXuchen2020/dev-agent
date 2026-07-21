using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Security;

/// <summary>
/// Service that sanitizes user inputs to prevent prompt injection attacks.
/// </summary>
public sealed partial class PromptInjectionService
{
    private readonly ILogger<PromptInjectionService> _logger;

    public PromptInjectionService(ILogger<PromptInjectionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sanitizes user message content by stripping or blocking known prompt injection patterns.
    /// Returns sanitized content. If the content is too dangerous, returns empty string.
    /// </summary>
    public string SanitizeUserMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;

        // Block known injection patterns
        var injectionPatterns = new[]
        {
            IgnorePreviousInstructionsPattern(),
            SystemOverridePattern(),
            RoleImpersonationPattern(),
            DelimiterBreakoutPattern()
        };

        foreach (var pattern in injectionPatterns)
        {
            if (pattern.IsMatch(content))
            {
                _logger.LogWarning("Prompt injection pattern detected and blocked: {Pattern}", pattern.ToString());
                return string.Empty;  // Block the message entirely
            }
        }

        // Strip excessive whitespace
        content = ExcessiveWhitespacePattern().Replace(content, " ").Trim();

        // Enforce length limit
        if (content.Length > 32000)
        {
            _logger.LogWarning("Message too long ({Length} chars), truncating", content.Length);
            content = content[..32000];
        }

        return content;
    }

    /// <summary>
    /// Wraps system prompt in XML boundaries for isolation.
    /// </summary>
    public static string WrapSystemPrompt(string systemPrompt)
    {
        return $"<system>\n{systemPrompt}\n</system>";
    }

    [GeneratedRegex(@"ignore\s+(all\s+)?previous\s+(instructions|prompts|directions)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IgnorePreviousInstructionsPattern();

    [GeneratedRegex(@"you\s+are\s+(not\s+)?(an?\s+)?(AI\s+)?(assistant|chatbot|model|system)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SystemOverridePattern();

    [GeneratedRegex(@"(system|user|assistant)\s*:.*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoleImpersonationPattern();

    // 收窄为只匹配真正的"提示边界分隔符注入"形态（对话模板分隔符 / 角色边界标记），
    // 不再误伤普通含括号/JSON/代码块的合法消息。其余三种正则保持不变。
    [GeneratedRegex(
        @"<\|\s*im_(start|end)\s*\|>|<<\s*sys\s*>>|<<\s*/\s*sys\s*>>|\[\s*inst\s*\]|\[\s*/\s*inst\s*\]|<\s*/\s*(system|user|assistant|bot)\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DelimiterBreakoutPattern();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex ExcessiveWhitespacePattern();
}
