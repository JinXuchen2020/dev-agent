#nullable disable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Research;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Handlers;

public class ResearchCommandHandlerTests
{
    private static ResearchCommandHandler Handler(
        IModelClient modelClient,
        ISearchProvider searchProvider,
        out ITokenCounter tokenCounter)
    {
        tokenCounter = Substitute.For<ITokenCounter>();
        tokenCounter.CountTokens(Arg.Any<string>()).Returns(1);
        return new ResearchCommandHandler(
            modelClient,
            searchProvider,
            tokenCounter,
            Options.Create(new StateMachineSettings()),
            Options.Create(new SearchSettings { DefaultMaxResults = 5 }),
            Substitute.For<ILogger<ResearchCommandHandler>>());
    }

    [Fact]
    public async Task Handle_Plans_Searches_Synthesizes_And_Streams_Events()
    {
        var modelClient = Substitute.For<IModelClient>();
        var planThenSynthesize = new Queue<ModelResponse>(new[]
        {
            new ModelResponse("[\"q1\",\"q2\"]", null, "m", "stop"),
            new ModelResponse("## 结论\n这是结论正文。", null, "m", "stop")
        });
        modelClient.ChatAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns<Task<ModelResponse>>(_ => Task.FromResult(planThenSynthesize.Dequeue()));

        var searchProvider = Substitute.For<ISearchProvider>();
        var perQueryResults = new Queue<SearchResult>(new[]
        {
            new SearchResult(true, new List<SearchSnippet>
            {
                new("T1", "http://a1", "S1"),
                new("T2", "http://b1", "S2")
            }),
            new SearchResult(true, new List<SearchSnippet>
            {
                new("T3", "http://a2", "S3"),
                new("T4", "http://b2", "S4")
            })
        });
        searchProvider.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(perQueryResults.Dequeue()));

        var handler = Handler(modelClient, searchProvider, out _);
        var command = new ResearchCommand("什么是量子计算？");

        var events = new List<ResearchProgressEvent>();
        await foreach (var e in await handler.Handle(command, default))
            events.Add(e);

        // Event sequence
        Assert.Equal(ResearchEventType.Plan, events[0].Type);
        Assert.NotNull(events[0].Queries);
        Assert.Equal(2, events[0].Queries.Count);

        Assert.Equal(2, events.Count(e => e.Type == ResearchEventType.SearchStart));
        Assert.Equal(2, events.Count(e => e.Type == ResearchEventType.SearchDone));
        Assert.All(events.Where(e => e.Type == ResearchEventType.SearchDone), e => Assert.Equal(2, e.SnippetCount));

        Assert.Contains(events, e => e.Type == ResearchEventType.Synthesize);
        var reportEvt = events.Single(e => e.Type == ResearchEventType.Report);
        Assert.NotNull(reportEvt.Report);
        Assert.Equal(4, reportEvt.Report.Sources.Count);          // 2 queries x 2 snippets
        Assert.Equal(2, reportEvt.Report.StepsUsed);
        Assert.Single(reportEvt.Report.Sections);                  // "结论"
        Assert.Equal("结论", reportEvt.Report.Sections[0].Heading);

        await searchProvider.Received(2).SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PlanFailure_Yields_Error_Then_EmptyReport()
    {
        var modelClient = Substitute.For<IModelClient>();
        modelClient.ChatAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns<Task<ModelResponse>>(_ => throw new System.InvalidOperationException("model down"));

        var searchProvider = Substitute.For<ISearchProvider>();
        var handler = Handler(modelClient, searchProvider, out _);
        var command = new ResearchCommand("x");

        var events = new List<ResearchProgressEvent>();
        await foreach (var e in await handler.Handle(command, default))
            events.Add(e);

        Assert.Contains(events, e => e.Type == ResearchEventType.Error && e.Error == "model down");
        var report = events.Single(e => e.Type == ResearchEventType.Report).Report;
        Assert.NotNull(report);
        Assert.Empty(report.Sources);
    }

    [Fact]
    public async Task Handle_SearchFailure_PerStep_IsTolerated_And_Still_Reports()
    {
        var modelClient = Substitute.For<IModelClient>();
        var planThenSynthesize3 = new Queue<ModelResponse>(new[]
        {
            new ModelResponse("[\"q1\"]", null, "m", "stop"),
            new ModelResponse("## 结论\nok", null, "m", "stop")
        });
        modelClient.ChatAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns<Task<ModelResponse>>(_ => Task.FromResult(planThenSynthesize3.Dequeue()));

        var searchProvider = Substitute.For<ISearchProvider>();
        searchProvider.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SearchResult(false, System.Array.Empty<SearchSnippet>(), "网络不可达")));

        var handler = Handler(modelClient, searchProvider, out _);
        var command = new ResearchCommand("x");

        var events = new List<ResearchProgressEvent>();
        await foreach (var e in await handler.Handle(command, default))
            events.Add(e);

        var done = events.Single(e => e.Type == ResearchEventType.SearchDone);
        Assert.Equal("检索失败：网络不可达", done.Message);
        var report = events.Single(e => e.Type == ResearchEventType.Report).Report;
        Assert.NotNull(report);
        Assert.Empty(report.Sources); // search failed -> no sources
        Assert.DoesNotContain(events, e => e.Type == ResearchEventType.Error);
    }
}
