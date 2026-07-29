namespace AgentPlatform.Application;

/// <summary>
/// 提交的 YAML 内容无法通过解析校验（格式非法）。
/// 由 API 层的 <c>InvalidYamlExceptionHandler</c> 映射为 400 BadRequest。
/// 定义在 Application 层以便命令处理程序（位于 Application）抛出，而无需反向依赖 Api 层。
/// </summary>
public sealed class InvalidYamlException : Exception
{
    /// <summary>构造 YAML 非法异常。</summary>
    /// <param name="detail">触发异常的字段或原因描述。</param>
    public InvalidYamlException(string detail)
        : base($"提供的 YAML 内容无效：{detail}") { }
}
