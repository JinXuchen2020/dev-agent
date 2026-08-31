using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AgentPlatform.Application.Workspaces;
using AgentPlatform.Application.Workspaces.Commands.AddWorkspaceMember;
using AgentPlatform.Application.Workspaces.Commands.CreateWorkspace;
using AgentPlatform.Application.Workspaces.Commands.DeleteWorkspace;
using AgentPlatform.Application.Workspaces.Commands.RemoveWorkspaceMember;
using AgentPlatform.Application.Workspaces.Commands.SwitchWorkspace;
using AgentPlatform.Application.Workspaces.Commands.UpdateWorkspace;
using AgentPlatform.Application.Workspaces.Queries.ListWorkspaceMembers;
using AgentPlatform.Application.Workspaces.Queries.ListWorkspaces;
using AgentPlatform.Infrastructure.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// API controller for workspace management (F35).
/// All routes are prefixed with <c>api/v1/workspaces</c>.
/// Visibility: Admin sees/operates all tenant workspaces; non-Admin can list/switch only the
/// default workspace plus workspaces they are a member of (decision D3=B).
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/workspaces")]
public sealed class WorkspacesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IJwtTokenService _tokenService;

    /// <summary>Initializes a new instance of the <see cref="WorkspacesController"/> class.</summary>
    public WorkspacesController(IMediator mediator, IJwtTokenService tokenService)
    {
        _mediator = mediator;
        _tokenService = tokenService;
    }

    /// <summary>Lists workspaces visible to the caller (Admin: all tenant workspaces).</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (userId, isAdmin) = ResolvePrincipal();
        var results = await _mediator.Send(new ListWorkspacesQuery(userId, isAdmin), ct);
        return Ok(results);
    }

    /// <summary>Creates a new workspace (Admin only).</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWorkspaceRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateWorkspaceCommand(request.Name, request.Description), ct);
        return result.Outcome switch
        {
            CreateWorkspaceOutcome.Created => CreatedAtAction(
                nameof(List), new { id = result.Workspace!.Id }, result.Workspace),
            _ => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "名称冲突",
                Detail = "同租户下已存在同名工作空间，请更换名称。"
            }),
        };
    }

    /// <summary>Renames / re-describes a workspace (Admin only).</summary>
    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWorkspaceRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateWorkspaceCommand(id, request.Name, request.Description), ct);
        return result.Outcome switch
        {
            UpdateWorkspaceOutcome.Updated => Ok(result.Workspace),
            UpdateWorkspaceOutcome.NotFound => NotFound(),
            _ => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "名称冲突",
                Detail = "同租户下已存在同名工作空间，请更换名称。"
            }),
        };
    }

    /// <summary>
    /// Deletes an empty, non-default workspace (Admin only). The default workspace and
    /// workspaces that still contain members or business entities are rejected with 409;
    /// cascading deletes are never performed (decision D4).
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var outcome = await _mediator.Send(new DeleteWorkspaceCommand(id), ct);
        return outcome switch
        {
            WorkspaceDeletionOutcome.Deleted => NoContent(),
            WorkspaceDeletionOutcome.NotFound => NotFound(),
            WorkspaceDeletionOutcome.DefaultConflict => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "默认工作空间不可删除",
                Detail = "默认工作空间承载存量数据且为兜底上下文，恒不可删除。"
            }),
            _ => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "工作空间仍被占用",
                Detail = "该工作空间内仍有成员或业务实体（Agent/工作流等），请先迁移或清空后再删除。"
            }),
        };
    }

    /// <summary>Lists the members of a workspace (Admin only).</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> ListMembers(Guid id, CancellationToken ct)
    {
        var members = await _mediator.Send(new ListWorkspaceMembersQuery(id), ct);
        if (members is null)
            return NotFound();
        return Ok(members);
    }

    /// <summary>Assigns a tenant user (located by email) to a workspace (Admin only).</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/members")]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddWorkspaceMemberRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AddWorkspaceMemberCommand(id, request.Email), ct);
        return result.Outcome switch
        {
            AddWorkspaceMemberOutcome.Added => Created($"api/v1/workspaces/{id}/members/{result.Member!.UserId}", result.Member),
            AddWorkspaceMemberOutcome.AlreadyMember => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "已是成员",
                Detail = "该用户已经是此工作空间的成员。"
            }),
            _ => NotFound(),
        };
    }

    /// <summary>Removes a member from a workspace (Admin only).</summary>
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken ct)
    {
        var removed = await _mediator.Send(new RemoveWorkspaceMemberCommand(id, userId), ct);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>
    /// Switches the caller's active workspace: validates visibility, re-issues the JWT with an
    /// updated workspace_id claim and refreshes the httpOnly cookie (decision D1=C).
    /// </summary>
    [HttpPost("{id:guid}/switch")]
    public async Task<IActionResult> Switch(Guid id, CancellationToken ct)
    {
        var (userId, isAdmin) = ResolvePrincipal();
        var workspace = await _mediator.Send(new SwitchWorkspaceCommand(id, userId, isAdmin), ct);
        if (workspace is null)
            return NotFound();

        var principal = HttpContext.User;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
            // API-Key 主体无 Name/Email claim：Claim 构造函数对 null value 抛异常，回退空串。
            new(ClaimTypes.Name, principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty),
            new(ClaimTypes.Email, principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty),
            new("sub", principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty),
            new("tenant_id", principal.FindFirstValue("tenant_id")!),
            new(ClaimTypes.Role, principal.FindFirstValue(ClaimTypes.Role)!),
            new("workspace_id", workspace.Id.ToString()),
        };
        var token = _tokenService.CreateToken(claims);

        // httpOnly + SameSite=Lax cookie; Secure is auto-enabled over HTTPS. Same options as login.
        HttpContext.Response.Cookies.Append("ap_access_token", token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = HttpContext.Request.IsHttps,
            Path = "/",
            MaxAge = TimeSpan.FromHours(1),
        });

        return Ok(new SwitchWorkspaceResponse(workspace, token));
    }

    private (Guid UserId, bool IsAdmin) ResolvePrincipal()
    {
        var principal = HttpContext.User;
        var userIdRaw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        _ = Guid.TryParse(userIdRaw, out var userId);
        var isAdmin = string.Equals(principal.FindFirstValue(ClaimTypes.Role), "Admin", StringComparison.OrdinalIgnoreCase);
        return (userId, isAdmin);
    }
}

/// <summary>Request body for creating a workspace.</summary>
public sealed record CreateWorkspaceRequest(
    [Required]
    [StringLength(100, MinimumLength = 1)]
    string Name,
    [StringLength(500)]
    string? Description);

/// <summary>Request body for updating a workspace.</summary>
public sealed record UpdateWorkspaceRequest(
    [Required]
    [StringLength(100, MinimumLength = 1)]
    string Name,
    [StringLength(500)]
    string? Description);

/// <summary>Request body for adding a member by email.</summary>
public sealed record AddWorkspaceMemberRequest(
    [Required]
    [EmailAddress]
    string Email);

/// <summary>Response from <c>POST /{id}/switch</c>.</summary>
public sealed record SwitchWorkspaceResponse(WorkspaceDto Workspace, string Token);
