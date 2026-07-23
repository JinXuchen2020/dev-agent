namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 从原始文档字节提取纯文本，供向量入库前的切分使用。
/// 按文件扩展名 / Content-Type 分发到具体实现（PDF / HTML / 纯文本）。
/// </summary>
public interface IDocumentTextExtractor
{
    /// <summary>是否支持该格式（用于分发，控制器取第一个返回 true 的提取器）。</summary>
    bool Supports(string fileName, string contentType);

    /// <summary>从字节流提取纯文本；不支持的格式抛 <see cref="NotSupportedException"/>。</summary>
    string Extract(Stream content, string fileName, string contentType);
}
