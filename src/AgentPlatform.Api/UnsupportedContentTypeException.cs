namespace AgentPlatform.Api;

/// <summary>
/// 上传了当前系统不支持提取文本的文档格式（如 .docx / .png 等二进制）。
/// 控制器捕获后映射为 400 BadRequest。
/// </summary>
public sealed class UnsupportedContentTypeException : Exception
{
    /// <summary>构造不支持文档格式异常。</summary>
    /// <param name="detail">触发异常的文件名或 Content-Type。</param>
    public UnsupportedContentTypeException(string detail)
        : base($"不支持的文档格式：{detail}") { }
}
