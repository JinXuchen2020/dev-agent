using System.IO.Compression;
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
    public void PdfText_Extracts_From_Uncompressed_Stream()
    {
        var pdf = BuildUncompressedPdf("Hello PDF World");
        var text = PdfTextExtractor.ExtractText(pdf);
        Assert.Contains("Hello PDF World", text);
    }

    [Fact]
    public void PdfText_Extracts_From_FlateDecode_Stream()
    {
        var inner = "BT /F1 12 Tf 50 50 Td (Compressed PDF Text) Tj ET";
        var compressed = Compress(inner);
        var pdf = BuildFlatePdf(compressed);
        var text = PdfTextExtractor.ExtractText(pdf);
        Assert.Contains("Compressed PDF Text", text);
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

    private static byte[] BuildUncompressedPdf(string text)
    {
        var content = $"BT /F1 12 Tf 50 50 Td ({text}) Tj ET";
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        sb.Append("1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n");
        sb.Append("2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n");
        sb.Append("3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\n");
        sb.Append($"4 0 obj<</Length {content.Length}>>stream\n");
        sb.Append(content).Append('\n');
        sb.Append("endstream endobj\n");
        sb.Append("trailer<</Root 1 0 R>>\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static byte[] BuildFlatePdf(byte[] compressed)
    {
        var header = "%PDF-1.4\n" +
                     "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
                     "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
                     "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\n" +
                     $"4 0 obj<</Filter /FlateDecode/Length {compressed.Length}>>stream\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        var footerBytes = Encoding.ASCII.GetBytes("\nendstream endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

        var result = new byte[headerBytes.Length + compressed.Length + footerBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, result, 0, headerBytes.Length);
        Buffer.BlockCopy(compressed, 0, result, headerBytes.Length, compressed.Length);
        Buffer.BlockCopy(footerBytes, 0, result, headerBytes.Length + compressed.Length, footerBytes.Length);
        return result;
    }

    private static byte[] Compress(string s)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal))
        {
            var bytes = Encoding.ASCII.GetBytes(s);
            zlib.Write(bytes, 0, bytes.Length);
        }
        return ms.ToArray();
    }
}
