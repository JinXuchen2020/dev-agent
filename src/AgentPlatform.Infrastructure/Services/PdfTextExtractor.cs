using System.IO;
using System.Text;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace AgentPlatform.Infrastructure.Services;

/// <summary>
/// PDF 文本提取器，基于 PdfPig（纯托管、零原生依赖）。
/// 正确处理 FlateDecode/LZW/ASCII85 压缩流、CID 复合字体 + ToUnicode 映射
/// （覆盖中文 / Office / WPS 导出的十六进制字符串文本）、以及标准加密 PDF 的密码解析。
/// 失败场景（加密且无密码、扫描件 / 图片型 PDF、损坏文件）返回空字符串，
/// 由控制器给出明确提示，而非静默丢弃。
/// </summary>
internal sealed class PdfTextExtractor : IDocumentTextExtractor
{
    private readonly ILogger<PdfTextExtractor>? _logger;

    public PdfTextExtractor(ILogger<PdfTextExtractor>? logger = null) => _logger = logger;

    public bool Supports(string fileName, string contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ext = Path.GetExtension(fileName);
        return string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public string Extract(Stream content, string fileName, string contentType)
    {
        try
        {
            using var document = PdfDocument.Open(content);
            var sb = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                var text = page.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.Append(text).Append('\n');
                }
            }

            return sb.ToString().Trim();
        }
        catch (PdfDocumentEncryptedException ex)
        {
            // 加密 PDF 且无密码：PdfPig 无法解密，返回空交由调用方提示用户先解密。
            _logger?.LogWarning(ex, "PDF 已加密，缺少密码，无法提取文本");
            return string.Empty;
        }
        catch (Exception ex)
        {
            // 损坏 / 不支持的结构：避免整个上传链路崩溃，返回空。
            _logger?.LogWarning(ex, "PDF 文本提取失败，已返回空内容");
            return string.Empty;
        }
    }
}
