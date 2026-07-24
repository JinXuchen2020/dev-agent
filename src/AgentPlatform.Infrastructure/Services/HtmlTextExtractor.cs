using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Infrastructure.Services;

/// <summary>
/// HTML 提取器：去除 &lt;script&gt;/&lt;style&gt;，剥离标签，解码 HTML 实体，归一化空白。
/// 轻量实现，避免引入额外依赖；足以满足 RAG 入库的纯文本需求。
/// </summary>
internal sealed class HtmlTextExtractor : IDocumentTextExtractor
{
    private static readonly Regex ScriptStyle =
        new(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex Tags = new(@"<[^>]+>");
    private static readonly Regex RunsOfSpace = new(@"[ \t]+");
    private static readonly Regex RunsOfNewline = new(@"(\r?\n[ \t]*){2,}");

    public bool Supports(string fileName, string contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && contentType.Equals("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ext = Path.GetExtension(fileName);
        return string.Equals(ext, ".html", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".htm", StringComparison.OrdinalIgnoreCase);
    }

    public string Extract(Stream content, string fileName, string contentType)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return ExtractText(reader.ReadToEnd());
    }

    internal static string ExtractText(string html)
    {
        html = ScriptStyle.Replace(html, " ");
        html = WebUtility.HtmlDecode(html);
        html = Tags.Replace(html, " ");
        html = RunsOfSpace.Replace(html, " ");
        html = RunsOfNewline.Replace(html, "\n\n");
        return html.Trim();
    }
}
