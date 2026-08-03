using System.Net;

namespace AgentPlatform.Domain;

/// <summary>
/// 已发布工作流相关领域异常（F22）。携带目标 HTTP 状态码，由
/// <see cref="AgentPlatform.Api.Exceptions.PublishedWorkflowExceptionHandler"/> 统一映射为
/// RFC 9457 ProblemDetails，经 <c>app.UseExceptionHandler()</c> 中间件返回。
/// </summary>
public sealed class PublishedWorkflowException : Exception
{
    /// <summary>获取应映射的 HTTP 状态码。</summary>
    public HttpStatusCode StatusCode { get; }

    public PublishedWorkflowException(string message, HttpStatusCode statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>构造 HTTP 400 Bad Request 异常。</summary>
    public static PublishedWorkflowException BadRequest(string message) =>
        new(message, HttpStatusCode.BadRequest);

    /// <summary>构造 HTTP 404 Not Found 异常。</summary>
    public static PublishedWorkflowException NotFound(string message) =>
        new(message, HttpStatusCode.NotFound);

    /// <summary>构造 HTTP 409 Conflict 异常。</summary>
    public static PublishedWorkflowException Conflict(string message) =>
        new(message, HttpStatusCode.Conflict);

    /// <summary>构造 HTTP 403 Forbidden 异常。</summary>
    public static PublishedWorkflowException Forbidden(string message) =>
        new(message, HttpStatusCode.Forbidden);
}
