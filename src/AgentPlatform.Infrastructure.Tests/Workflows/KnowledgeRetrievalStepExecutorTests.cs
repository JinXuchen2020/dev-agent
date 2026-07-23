#nullable disable
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.KnowledgeBases;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Workflows;

public class KnowledgeRetrievalStepExecutorTests
{
    private static WorkflowContext Ctx(Guid tenantId, params (string Name, int Order, string Content)[] artifacts)
    {
        var dict = new Dictionary<string, StepArtifact>();
        foreach (var a in artifacts)
        {
            dict[a.Name] = new StepArtifact
            {
                StepName = a.Name,
                StepOrder = a.Order,
                Content = a.Content,
                ContentType = "general"
            };
        }

        return new WorkflowContext
        {
            WorkflowId = Guid.NewGuid(),
            CurrentStepOrder = 0,
            Artifacts = dict,
            Blackboard = Blackboard.Empty,
            Retrieval = RetrievalContext.Empty,
            Summary = StepHistory.Empty,
            TenantId = tenantId
        };
    }

    private static IWorkflowExecutable Node(string configJson)
    {
        var step = Substitute.For<IWorkflowExecutable>();
        step.Name.Returns("知识检索");
        step.ConfigJson.Returns(configJson);
        return step;
    }

    private static KnowledgeRetrievalStepExecutor Executor(
        IKnowledgeBaseRepository repo, IVectorStore vector)
    {
        return new KnowledgeRetrievalStepExecutor(
            Substitute.For<ILogger<KnowledgeRetrievalStepExecutor>>(),
            repo, vector, Options.Create(new RagSettings()));
    }

    [Fact]
    public async Task ExecuteAsync_Retrieves_From_KnowledgeBase_And_Returns_Context()
    {
        var tenantId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var collection = "kb-collection-1";
        var kb = KnowledgeBase.Create(tenantId, "KB", "d", collection, "text-embedding-3-small");

        var repo = Substitute.For<IKnowledgeBaseRepository>();
        repo.GetByIdAsync(kbId, Arg.Any<CancellationToken>()).Returns(kb);

        var vector = Substitute.For<IVectorStore>();
        vector.SearchAsync(collection, Arg.Any<string>(), tenantId, Arg.Any<int>(), Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult> { new("doc1", "RAG 相关知识片段", 0.9) });

        var result = await Executor(repo, vector)
            .ExecuteAsync(Node($"{{\"knowledgeBaseId\":\"{kbId}\",\"query\":\"测试查询\"}}"), Ctx(tenantId));

        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.Contains("RAG 相关知识片段", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_Uses_Upstream_Artifact_As_Query_When_No_ExplicitQuery()
    {
        var tenantId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var collection = "kb-collection-2";
        var kb = KnowledgeBase.Create(tenantId, "KB", "d", collection, "text-embedding-3-small");

        var repo = Substitute.For<IKnowledgeBaseRepository>();
        repo.GetByIdAsync(kbId, Arg.Any<CancellationToken>()).Returns(kb);

        var vector = Substitute.For<IVectorStore>();
        vector.SearchAsync(Arg.Any<string>(), Arg.Any<string>(), tenantId, Arg.Any<int>(), Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>());

        await Executor(repo, vector).ExecuteAsync(
            Node($"{{\"knowledgeBaseId\":\"{kbId}\"}}"),
            Ctx(tenantId, ("上游节点", 0, "上游上下文内容")));

        await vector.Received(1).SearchAsync(
            Arg.Any<string>(),
            Arg.Is<string>(q => q.Contains("上游上下文内容")),
            tenantId, Arg.Any<int>(), Arg.Any<double?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_CrossTenant_KnowledgeBase_Returns_FatalFailure()
    {
        var tenantId = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var kb = KnowledgeBase.Create(otherTenant, "KB", "d", "kb-x", "text-embedding-3-small");

        var repo = Substitute.For<IKnowledgeBaseRepository>();
        repo.GetByIdAsync(kbId, Arg.Any<CancellationToken>()).Returns(kb);

        var vector = Substitute.For<IVectorStore>();
        var result = await Executor(repo, vector)
            .ExecuteAsync(Node($"{{\"knowledgeBaseId\":\"{kbId}\"}}"), Ctx(tenantId));

        Assert.Equal(StepOutcome.FailedRollback, result.Outcome);
        await vector.Received(0).SearchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<double?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_No_Query_And_No_Upstream_Returns_FatalFailure()
    {
        var tenantId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var kb = KnowledgeBase.Create(tenantId, "KB", "d", "kb-y", "text-embedding-3-small");

        var repo = Substitute.For<IKnowledgeBaseRepository>();
        repo.GetByIdAsync(kbId, Arg.Any<CancellationToken>()).Returns(kb);

        var vector = Substitute.For<IVectorStore>();
        var result = await Executor(repo, vector)
            .ExecuteAsync(Node($"{{\"knowledgeBaseId\":\"{kbId}\"}}"), Ctx(tenantId));

        Assert.Equal(StepOutcome.FailedRollback, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_Empty_Results_Returns_Success_With_Empty_Output()
    {
        var tenantId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var kb = KnowledgeBase.Create(tenantId, "KB", "d", "kb-z", "text-embedding-3-small");

        var repo = Substitute.For<IKnowledgeBaseRepository>();
        repo.GetByIdAsync(kbId, Arg.Any<CancellationToken>()).Returns(kb);

        var vector = Substitute.For<IVectorStore>();
        vector.SearchAsync(Arg.Any<string>(), Arg.Any<string>(), tenantId, Arg.Any<int>(), Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>());

        var result = await Executor(repo, vector)
            .ExecuteAsync(Node($"{{\"knowledgeBaseId\":\"{kbId}\",\"query\":\"测试\"}}"), Ctx(tenantId));

        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.Equal(string.Empty, result.Output);
    }
}
