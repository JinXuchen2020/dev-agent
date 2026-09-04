using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workspaces.Commands.AddWorkspaceMember;

/// <summary>Outcome of an <see cref="AddWorkspaceMemberCommand"/>.</summary>
public enum AddWorkspaceMemberOutcome
{
    /// <summary>Member added successfully.</summary>
    Added,

    /// <summary>No workspace with the supplied id was found in the tenant.</summary>
    WorkspaceNotFound,

    /// <summary>No user with the supplied email was found in the tenant.</summary>
    UserNotFound,

    /// <summary>The user is already a member of the workspace.</summary>
    AlreadyMember,
}

/// <summary>Result of <see cref="AddWorkspaceMemberCommand"/> (DTO populated on success).</summary>
public sealed record AddWorkspaceMemberResult(AddWorkspaceMemberOutcome Outcome, WorkspaceMemberDto? Member);

/// <summary>
/// Command to assign a tenant user (located by email) to a workspace (Admin only, enforced at the API edge).
/// </summary>
public sealed record AddWorkspaceMemberCommand(Guid WorkspaceId, string Email)
    : ICommand<AddWorkspaceMemberResult>;

internal sealed class AddWorkspaceMemberCommandHandler(
    IWorkspaceRepository workspaceRepo,
    IWorkspaceMemberRepository memberRepo,
    IUserRepository userRepo,
    ITenantProvider tenantProvider)
    : IRequestHandler<AddWorkspaceMemberCommand, AddWorkspaceMemberResult>
{
    public async Task<AddWorkspaceMemberResult> Handle(AddWorkspaceMemberCommand request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email);

        var workspace = await workspaceRepo.GetByIdAsync(request.WorkspaceId, ct);
        if (workspace is null)
            return new AddWorkspaceMemberResult(AddWorkspaceMemberOutcome.WorkspaceNotFound, null);

        var user = await userRepo.GetByEmailAsync(tenantProvider.GetTenantId(), request.Email.Trim(), ct);
        if (user is null)
            return new AddWorkspaceMemberResult(AddWorkspaceMemberOutcome.UserNotFound, null);

        if (await memberRepo.IsMemberAsync(workspace.Id, user.Id, ct))
            return new AddWorkspaceMemberResult(AddWorkspaceMemberOutcome.AlreadyMember, null);

        var member = new Domain.Aggregates.Workspaces.WorkspaceMember(
            Guid.NewGuid(), tenantProvider.GetTenantId(), workspace.Id, user.Id);
        await memberRepo.AddAsync(member, ct);

        return new AddWorkspaceMemberResult(
            AddWorkspaceMemberOutcome.Added,
            new WorkspaceMemberDto(user.Id, user.Email, member.CreatedAt));
    }
}
