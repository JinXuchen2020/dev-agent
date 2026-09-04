using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workspaces.Commands.AddWorkspaceMember;
using AgentPlatform.Application.Workspaces.Commands.CreateWorkspace;
using AgentPlatform.Application.Workspaces.Commands.DeleteWorkspace;
using AgentPlatform.Application.Workspaces.Commands.SwitchWorkspace;
using AgentPlatform.Application.Workspaces.Commands.UpdateWorkspace;
using AgentPlatform.Application.Workspaces.Queries.ListWorkspaces;
using AgentPlatform.Application.Workspaces;
using AgentPlatform.Domain.Aggregates.Users;
using AgentPlatform.Domain.Aggregates.Workspaces;
using AgentPlatform.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Workspaces;

/// <summary>
/// F35 workspace command/query handler tests: creation conflicts, deletion guards
/// (default conflict / in-use conflict), non-Admin visibility (D3=B), and switch validation.
/// </summary>
public class WorkspaceHandlersTests
{
    private static (WorkspaceDto Dto, Workspace Aggregate) MakeWorkspace(string name, bool isDefault = false)
    {
        var ws = new Workspace(Guid.NewGuid(), Guid.NewGuid(), name, isDefault: isDefault);
        return (new WorkspaceDto(ws.Id, ws.Name, ws.Description, ws.IsDefault, ws.CreatedAt), ws);
    }

    [Fact]
    public async Task CreateWorkspace_With_Duplicate_Name_Returns_NameConflict()
    {
        var repo = Substitute.For<IWorkspaceRepository>();
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.GetTenantId().Returns(Guid.NewGuid());
        repo.NameExistsAsync("ws", Arg.Any<CancellationToken>()).Returns(true);

        var handler = new CreateWorkspaceCommandHandler(repo, tenantProvider);
        var result = await handler.Handle(new CreateWorkspaceCommand("ws", null), CancellationToken.None);

        Assert.Equal(CreateWorkspaceOutcome.NameConflict, result.Outcome);
        Assert.Null(result.Workspace);
        await repo.DidNotReceive().AddAsync(Arg.Any<Workspace>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateWorkspace_Success_Returns_Dto_And_Persists()
    {
        var tenantId = Guid.NewGuid();
        var repo = Substitute.For<IWorkspaceRepository>();
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.GetTenantId().Returns(tenantId);
        repo.NameExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var handler = new CreateWorkspaceCommandHandler(repo, tenantProvider);
        var result = await handler.Handle(new CreateWorkspaceCommand("ws", "d"), CancellationToken.None);

        Assert.Equal(CreateWorkspaceOutcome.Created, result.Outcome);
        Assert.NotNull(result.Workspace);
        await repo.Received(1).AddAsync(
            Arg.Is<Workspace>(w => w.TenantId == tenantId && w.Name == "ws" && !w.IsDefault),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateWorkspace_Unknown_Id_Returns_NotFound()
    {
        var repo = Substitute.For<IWorkspaceRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Workspace?)null);

        var handler = new UpdateWorkspaceCommandHandler(repo);
        var result = await handler.Handle(new UpdateWorkspaceCommand(Guid.NewGuid(), "n", null), CancellationToken.None);

        Assert.Equal(UpdateWorkspaceOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task DeleteWorkspace_Default_Returns_DefaultConflict()
    {
        var (_, ws) = MakeWorkspace("Default", isDefault: true);
        var repo = Substitute.For<IWorkspaceRepository>();
        var memberRepo = Substitute.For<IWorkspaceMemberRepository>();
        repo.GetByIdAsync(ws.Id, Arg.Any<CancellationToken>()).Returns(ws);

        var handler = new DeleteWorkspaceCommandHandler(repo, memberRepo);
        var outcome = await handler.Handle(new DeleteWorkspaceCommand(ws.Id), CancellationToken.None);

        Assert.Equal(WorkspaceDeletionOutcome.DefaultConflict, outcome);
        repo.DidNotReceive().Remove(Arg.Any<Workspace>());
    }

    [Fact]
    public async Task DeleteWorkspace_With_Members_Returns_InUseConflict()
    {
        var (_, ws) = MakeWorkspace("Team");
        var repo = Substitute.For<IWorkspaceRepository>();
        var memberRepo = Substitute.For<IWorkspaceMemberRepository>();
        repo.GetByIdAsync(ws.Id, Arg.Any<CancellationToken>()).Returns(ws);
        memberRepo.CountByWorkspaceAsync(ws.Id, Arg.Any<CancellationToken>()).Returns(1);

        var handler = new DeleteWorkspaceCommandHandler(repo, memberRepo);
        var outcome = await handler.Handle(new DeleteWorkspaceCommand(ws.Id), CancellationToken.None);

        Assert.Equal(WorkspaceDeletionOutcome.InUseConflict, outcome);
        repo.DidNotReceive().Remove(Arg.Any<Workspace>());
    }

    [Fact]
    public async Task DeleteWorkspace_With_Business_Entities_Returns_InUseConflict()
    {
        var (_, ws) = MakeWorkspace("Team");
        var repo = Substitute.For<IWorkspaceRepository>();
        var memberRepo = Substitute.For<IWorkspaceMemberRepository>();
        repo.GetByIdAsync(ws.Id, Arg.Any<CancellationToken>()).Returns(ws);
        memberRepo.CountByWorkspaceAsync(ws.Id, Arg.Any<CancellationToken>()).Returns(0);
        repo.CountBusinessEntitiesAsync(ws.Id, Arg.Any<CancellationToken>()).Returns(3);

        var handler = new DeleteWorkspaceCommandHandler(repo, memberRepo);
        var outcome = await handler.Handle(new DeleteWorkspaceCommand(ws.Id), CancellationToken.None);

        Assert.Equal(WorkspaceDeletionOutcome.InUseConflict, outcome);
        repo.DidNotReceive().Remove(Arg.Any<Workspace>());
    }

    [Fact]
    public async Task DeleteWorkspace_Empty_NonDefault_Returns_Deleted()
    {
        var (_, ws) = MakeWorkspace("Team");
        var repo = Substitute.For<IWorkspaceRepository>();
        var memberRepo = Substitute.For<IWorkspaceMemberRepository>();
        repo.GetByIdAsync(ws.Id, Arg.Any<CancellationToken>()).Returns(ws);
        memberRepo.CountByWorkspaceAsync(ws.Id, Arg.Any<CancellationToken>()).Returns(0);
        repo.CountBusinessEntitiesAsync(ws.Id, Arg.Any<CancellationToken>()).Returns(0);

        var handler = new DeleteWorkspaceCommandHandler(repo, memberRepo);
        var outcome = await handler.Handle(new DeleteWorkspaceCommand(ws.Id), CancellationToken.None);

        Assert.Equal(WorkspaceDeletionOutcome.Deleted, outcome);
        repo.Received(1).Remove(ws);
    }

    [Fact]
    public async Task ListWorkspaces_NonAdmin_Sees_Default_Plus_Memberships()
    {
        var (_, defaultWs) = MakeWorkspace("Default", isDefault: true);
        var (_, joinedWs) = MakeWorkspace("Joined");
        var (_, hiddenWs) = MakeWorkspace("Hidden");

        var repo = Substitute.For<IWorkspaceRepository>();
        var memberRepo = Substitute.For<IWorkspaceMemberRepository>();
        repo.GetDefaultAsync(Arg.Any<CancellationToken>()).Returns(defaultWs);
        memberRepo.ListWorkspaceIdsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([joinedWs.Id]);
        repo.ListByIdsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.ToHashSet().SetEquals(new[] { defaultWs.Id, joinedWs.Id })),
            Arg.Any<CancellationToken>())
            .Returns(new List<Workspace> { defaultWs, joinedWs });

        var handler = new ListWorkspacesQueryHandler(repo, memberRepo);
        var result = await handler.Handle(new ListWorkspacesQuery(Guid.NewGuid(), IsAdmin: false), CancellationToken.None);

        var names = result.Select(w => w.Name).ToList();
        Assert.Contains("Default", names);
        Assert.Contains("Joined", names);
        Assert.DoesNotContain("Hidden", names);
    }

    [Fact]
    public async Task ListWorkspaces_Admin_Sees_All()
    {
        var (_, ws1) = MakeWorkspace("A");
        var (_, ws2) = MakeWorkspace("B");
        var repo = Substitute.For<IWorkspaceRepository>();
        var memberRepo = Substitute.For<IWorkspaceMemberRepository>();
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<Workspace> { ws1, ws2 });

        var handler = new ListWorkspacesQueryHandler(repo, memberRepo);
        var result = await handler.Handle(new ListWorkspacesQuery(Guid.NewGuid(), IsAdmin: true), CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SwitchWorkspace_NonAdmin_Not_Member_Of_NonDefault_Returns_Null()
    {
        var (_, ws) = MakeWorkspace("Team");
        var repo = Substitute.For<IWorkspaceRepository>();
        var memberRepo = Substitute.For<IWorkspaceMemberRepository>();
        repo.GetByIdAsync(ws.Id, Arg.Any<CancellationToken>()).Returns(ws);
        memberRepo.IsMemberAsync(ws.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var handler = new SwitchWorkspaceCommandHandler(repo, memberRepo);
        var result = await handler.Handle(
            new SwitchWorkspaceCommand(ws.Id, Guid.NewGuid(), IsAdmin: false), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SwitchWorkspace_NonAdmin_Member_Returns_Workspace()
    {
        var (_, ws) = MakeWorkspace("Team");
        var repo = Substitute.For<IWorkspaceRepository>();
        var memberRepo = Substitute.For<IWorkspaceMemberRepository>();
        repo.GetByIdAsync(ws.Id, Arg.Any<CancellationToken>()).Returns(ws);
        memberRepo.IsMemberAsync(ws.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var handler = new SwitchWorkspaceCommandHandler(repo, memberRepo);
        var result = await handler.Handle(
            new SwitchWorkspaceCommand(ws.Id, Guid.NewGuid(), IsAdmin: false), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ws.Id, result!.Id);
    }

    [Fact]
    public async Task AddWorkspaceMember_Unknown_Email_Returns_UserNotFound()
    {
        var (_, ws) = MakeWorkspace("Team");
        var repo = Substitute.For<IWorkspaceRepository>();
        var memberRepo = Substitute.For<IWorkspaceMemberRepository>();
        var userRepo = Substitute.For<IUserRepository>();
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.GetTenantId().Returns(Guid.NewGuid());
        repo.GetByIdAsync(ws.Id, Arg.Any<CancellationToken>()).Returns(ws);
        userRepo.GetByEmailAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var handler = new AddWorkspaceMemberCommandHandler(repo, memberRepo, userRepo, tenantProvider);
        var result = await handler.Handle(
            new AddWorkspaceMemberCommand(ws.Id, "nobody@test.io"), CancellationToken.None);

        Assert.Equal(AddWorkspaceMemberOutcome.UserNotFound, result.Outcome);
        await memberRepo.DidNotReceive().AddAsync(Arg.Any<WorkspaceMember>(), Arg.Any<CancellationToken>());
    }
}
