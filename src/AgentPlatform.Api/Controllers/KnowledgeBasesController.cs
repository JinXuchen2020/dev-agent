using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.KnowledgeBases.Commands.CreateKnowledgeBase;
using AgentPlatform.Application.KnowledgeBases.Commands.DeleteKnowledgeBase;
using AgentPlatform.Application.KnowledgeBases.Commands.UploadDocument;
using AgentPlatform.Application.KnowledgeBases.Queries.GetKnowledgeBase;
using AgentPlatform.Application.KnowledgeBases.Queries.ListKnowledgeBases;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// 知识库管理 API（建库、上传文档、列表、详情、删除）。
/// 所有路由前缀为 <c>api/v1/knowledge-bases</c>，受 JWT/API-Key 鉴权保护。
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/knowledge-bases")]
public sealed class KnowledgeBasesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantProvider _tenant;
    private readonly IEnumerable<IDocumentTextExtractor> _extractors;

    /// <summary>初始化 <see cref="KnowledgeBasesController"/> 的新实例。</summary>
    public KnowledgeBasesController(
        IMediator mediator,
        ITenantProvider tenant,
        IEnumerable<IDocumentTextExtractor> extractors)
    {
        _mediator = mediator;
        _tenant = tenant;
        _extractors = extractors;
    }

    /// <summary>创建新知识库。</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateKnowledgeBaseRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("name is required");

        var command = new CreateKnowledgeBaseCommand(
            _tenant.GetTenantId(), request.Name, request.Description, request.EmbeddingModel);
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>上传文档到知识库：切分并入库向量存储。</summary>
    [HttpPost("{id:guid}/documents")]
    [RequestSizeLimit(100 * 1024 * 1024)]
    public async Task<IActionResult> UploadDocument(
        Guid id,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("file is required");

        if (file.Length > 100 * 1024 * 1024)
            return BadRequest("file too large (max 100MB)");

        byte[] bytes;
        await using (var readStream = file.OpenReadStream())
        using (var ms = new MemoryStream())
        {
            await readStream.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        var extractor = _extractors.FirstOrDefault(e => e.Supports(file.FileName, file.ContentType))
            ?? throw new UnsupportedContentTypeException(file.ContentType ?? file.FileName);

        string content;
        using (var ms = new MemoryStream(bytes))
            content = extractor.Extract(ms, file.FileName, file.ContentType);

        if (string.IsNullOrWhiteSpace(content))
            return BadRequest("无法从文件中提取文本，请检查文件内容");

        var command = new UploadDocumentCommand(
            _tenant.GetTenantId(), id, file.FileName, file.ContentType, content);
        var result = await _mediator.Send(command, ct);
        if (result is null)
            return NotFound();
        return Ok(result);
    }

    /// <summary>列出当前租户下的所有知识库。</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListKnowledgeBasesQuery(_tenant.GetTenantId()), ct);
        return Ok(result);
    }

    /// <summary>获取单个知识库详情（含文档列表）。</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetKnowledgeBaseQuery(_tenant.GetTenantId(), id), ct);
        if (result is null)
            return NotFound();
        return Ok(result);
    }

    /// <summary>删除知识库（级联删除其向量分块）。</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _mediator.Send(new DeleteKnowledgeBaseCommand(_tenant.GetTenantId(), id), ct);
        if (!deleted)
            return NotFound();
        return NoContent();
    }
}

/// <summary>创建知识库请求体。</summary>
public sealed record CreateKnowledgeBaseRequest(
    string Name,
    string? Description = null,
    string? EmbeddingModel = null);
