using System.Reflection;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing;
using AgentPlatform.Infrastructure.Memory;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Memory;

/// <summary>
/// F33 ①：语义记忆服务——复用 IVectorStore 的租户隔离集合，episodic 写回与召回透传。
/// </summary>
public sealed class SemanticMemoryServiceTests
{
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly List<string> _ingestedIds = [];
    private readonly List<string> _ingestedContents = [];

    public SemanticMemoryServiceTests()
    {
        _vectorStore.IngestDocumentAsync(
                Arg.Any<string>(), Arg.Do<string>(_ingestedIds.Add), Arg.Do<string>(_ingestedContents.Add),
                Arg.Any<Guid>(), Arg.Any<Dictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task RememberRun_Ingests_Into_SemanticMemory_Collection()
    {
        var svc = new SemanticMemoryService(_vectorStore);
        var wfId = Guid.NewGuid();

        await svc.RememberRunAsync(TenantId(), wfId, "测试工作流", "completed",
            "- Architect: 架构设计产出");

        Assert.Single(_ingestedIds);
        Assert.Contains(_ingestedContents, c => c.Contains("[episodic:completed]"));
        Assert.Contains(_ingestedContents, c => c.Contains("测试工作流"));

        await _vectorStore.Received(1).IngestDocumentAsync(
            RoutingConstants.SemanticMemoryCollection, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Is<Dictionary<string, string>>(m =>
                m != null &&
                m.GetValueOrDefault("kind") == "run" &&
                m.GetValueOrDefault("outcome") == "completed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RememberRun_Deterministic_DocId_Dedupes_Same_Content()
    {
        var svc = new SemanticMemoryService(_vectorStore);
        var wfId = Guid.NewGuid();

        await svc.RememberRunAsync(TenantId(), wfId, "wf", "completed", "digest-1");
        await svc.RememberRunAsync(TenantId(), wfId, "wf", "completed", "digest-1");

        Assert.Equal(2, _ingestedIds.Count);
        Assert.Equal(_ingestedIds[0], _ingestedIds[1]); // 内容寻址：同输入同 id

        await svc.RememberRunAsync(TenantId(), wfId, "wf", "completed", "digest-2");
        Assert.NotEqual(_ingestedIds[0], _ingestedIds[2]);
    }

    [Fact]
    public async Task Recall_PassesThrough_To_VectorSearch_With_Collection_And_Params()
    {
        var svc = new SemanticMemoryService(_vectorStore);
        var tenant = TenantId(); // 固定租户：stub 与调用必须同一 Guid
        var expected = new List<VectorSearchResult>
        {
            new("doc-1", "[episodic:completed] …", 0.82)
        };
        _vectorStore.SearchAsync(
                RoutingConstants.SemanticMemoryCollection, "架构", tenant,
                3, 0.6, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await svc.RecallAsync(tenant, "架构", topK: 3, minScore: 0.6);

        Assert.Single(result);
        Assert.Equal("doc-1", result[0].DocumentId);
        Assert.Equal(0.82, result[0].Score);
    }

    private static Guid TenantId() => Guid.NewGuid();
}