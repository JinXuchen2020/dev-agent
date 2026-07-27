using System.IO;
using System.Text;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Services;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Services;

public class DocumentTextExtractorTests
{
    [Fact]
    public void PlainText_Supports_Text_Formats_And_Extracts_Verbatim()
    {
        var ex = new PlainTextExtractor();
        Assert.True(ex.Supports("a.txt", "text/plain"));
        Assert.True(ex.Supports("a.md", "text/markdown"));
        Assert.False(ex.Supports("a.pdf", "application/pdf"));

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("hello world 中文"));
        Assert.Equal("hello world 中文", ex.Extract(ms, "a.txt", "text/plain"));
    }

    [Fact]
    public void HtmlText_Strips_Tags_And_Scripts()
    {
        var ex = new HtmlTextExtractor();
        const string html =
            "<html><body><script>alert(1)</script><style>.x{color:red}</style>" +
            "<p>Hello <b>HTML</b> World &amp; Co</p></body></html>";

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(html));
        var text = ex.Extract(ms, "a.html", "text/html");

        Assert.Contains("Hello HTML World & Co", text);
        Assert.DoesNotContain("alert(1)", text);
        Assert.DoesNotContain("<", text);
        Assert.DoesNotContain("color:red", text);
    }

    [Fact]
    public void PdfText_Extracts_Chinese_Text_From_Real_Pdf()
    {
        // 真实中文 PDF（reportlab + STSong-Light CID 字体，文本以十六进制串存储）
        // 复现用户“无法从文件中提取文本”的根因：自研提取器不识别 <...> 十六进制串。
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "sample-chinese.pdf");
        Assert.True(File.Exists(path), $"测试 PDF 资源缺失: {path}");

        var ex = new PdfTextExtractor();
        using var ms = new MemoryStream(File.ReadAllBytes(path));
        var text = ex.Extract(ms, "sample-chinese.pdf", "application/pdf");

        Assert.Contains("中文 PDF 文本提取", text);
        Assert.Contains("Hello PDF World", text);
    }

    [Fact]
    public void PdfText_Returns_Empty_For_NonPdfBytes_Without_Throwing()
    {
        var ex = new PdfTextExtractor();
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("this is definitely not a pdf file"));
        var text = ex.Extract(ms, "bad.pdf", "application/pdf");
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void Dispatch_Resolves_Html_Before_Plain_For_TextHtml()
    {
        var extractors = new IDocumentTextExtractor[]
        {
            new PdfTextExtractor(), new HtmlTextExtractor(), new PlainTextExtractor()
        };

        Assert.IsType<HtmlTextExtractor>(extractors.First(e => e.Supports("a.html", "text/html")));
        Assert.IsType<PdfTextExtractor>(extractors.First(e => e.Supports("a.pdf", "application/pdf")));
        Assert.IsType<PlainTextExtractor>(extractors.First(e => e.Supports("a.txt", "text/plain")));
    }
}
