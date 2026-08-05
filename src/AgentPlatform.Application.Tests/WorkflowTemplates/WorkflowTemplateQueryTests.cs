using AgentPlatform.Application.Workflows.Versioning;
using AgentPlatform.Application.WorkflowTemplates;
using AgentPlatform.Domain.Aggregates.WorkflowTemplates;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.WorkflowTemplates;

/// <summary>
/// F23 单元测试：模板市场三个查询处理器（List / Get / Categories）。
/// 验证映射正确性、缺失模板幂等返回 null、分类枚举全量导出。
/// </summary>
public sealed class WorkflowTemplateQueryTests
{
    private static WorkflowTemplate BuildTemplate(
        Guid id, string name, WorkflowTemplateCategory cat, string snapshotJson,
        string? desc = null, IEnumerable<string>? tags = null) =>
        new(id, name, cat, desc, snapshotJson, tags);

    [Fact]
    public async Task List_ReturnsMappedSummaries()
    {
        var templates = new List<WorkflowTemplate>
        {
            BuildTemplate(Guid.NewGuid(), "A", WorkflowTemplateCategory.General, "{}", "desc-a"),
            BuildTemplate(Guid.NewGuid(), "B", WorkflowTemplateCategory.WebScraping, "{}", null, new[] { "scrape" }),
        };
        var repo = Substitute.For<IWorkflowTemplateRepository>();
        repo.ListAsync(Arg.Any<WorkflowTemplateCategory?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(templates);

        var handler = new ListWorkflowTemplatesQueryHandler(repo);
        var result = await handler.Handle(new ListWorkflowTemplatesQuery(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("A", result[0].Name);
        Assert.Equal(WorkflowTemplateCategory.WebScraping, result[1].Category);
        Assert.Equal(new[] { "scrape" }, result[1].Tags);
    }

    [Fact]
    public async Task Get_Existing_ReturnsDetailWithDecodedGraph()
    {
        var id = Guid.NewGuid();
        var start = Guid.NewGuid();
        var snapshot = new WorkflowGraphSnapshot(
            "ctx",
            new List<WorkflowVersionNode> { new(start, StepType.Start, "Start", 0, 0, "{}", null) },
            new List<WorkflowVersionEdge>());
        var template = BuildTemplate(id, "T", WorkflowTemplateCategory.General, snapshot.ToJson(), "d");
        var repo = Substitute.For<IWorkflowTemplateRepository>();
        repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(template);

        var handler = new GetWorkflowTemplateQueryHandler(repo);
        var result = await handler.Handle(new GetWorkflowTemplateQuery(id), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("ctx", result!.Context);
        Assert.Single(result.Nodes);
        Assert.Equal("Start", result.Nodes[0].Name);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNull()
    {
        var repo = Substitute.For<IWorkflowTemplateRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((WorkflowTemplate?)null);

        var handler = new GetWorkflowTemplateQueryHandler(repo);
        var result = await handler.Handle(new GetWorkflowTemplateQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task List_PassesCategoryAndKeywordToRepository()
    {
        var repo = Substitute.For<IWorkflowTemplateRepository>();
        repo.ListAsync(Arg.Any<WorkflowTemplateCategory?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<WorkflowTemplate>());

        var handler = new ListWorkflowTemplatesQueryHandler(repo);
        await handler.Handle(
            new ListWorkflowTemplatesQuery(WorkflowTemplateCategory.WebScraping, "scrape"), CancellationToken.None);

        _ = repo.Received(1).ListAsync(
            WorkflowTemplateCategory.WebScraping, "scrape", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Categories_ReturnsAllEnumValues()
    {
        var handler = new GetWorkflowTemplateCategoriesQueryHandler();
        var result = await handler.Handle(new GetWorkflowTemplateCategoriesQuery(), CancellationToken.None);

        Assert.Equal(8, result.Count); // 决策 S4：8 个硬编码枚举值
        Assert.Contains(result, o => o.Value == (int)WorkflowTemplateCategory.KnowledgeQa && o.Name == "KnowledgeQa");
        Assert.Contains(result, o => o.Value == (int)WorkflowTemplateCategory.DataAnalysis && o.Name == "DataAnalysis");
    }
}
