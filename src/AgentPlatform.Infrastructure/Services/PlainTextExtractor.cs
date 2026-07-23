using System.Text;
using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Infrastructure.Services;

/// <summary>
/// 纯文本兜底提取器：覆盖 .txt/.md/.csv/.json/.xml 等可直接按 UTF-8 读出的文本格式
/// （即原 <c>KnowledgeBasesController</c> 用 <c>StreamReader</c> 读取的行为）。
/// </summary>
internal sealed class PlainTextExtractor : IDocumentTextExtractor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".md", ".markdown", ".csv", ".json", ".xml", ".log", ".yml", ".yaml"
    };

    public bool Supports(string fileName, string contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext);
    }

    public string Extract(Stream content, string fileName, string contentType)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return reader.ReadToEnd();
    }
}
