using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Application.Research;

/// <summary>
/// Orchestrates the multi-step research loop. Returns an <see cref="IAsyncEnumerable{ResearchProgressEvent}"/>
/// so the controller can stream progress over SSE. The search step uses a <b>real</b> HTTP provider;
/// the LLM steps use the injected <see cref="IModelClient"/> (stubbable in tests).
/// </summary>
internal sealed class ResearchCommandHandler
    : IRequestHandler<ResearchCommand, IAsyncEnumerable<ResearchProgressEvent>>
{
    private readonly IModelClient _modelClient;
    private readonly ISearchProvider _searchProvider;
    private readonly ITokenCounter _tokenCounter;
    private readonly StateMachineSettings _stateSettings;
    private readonly SearchSettings _searchSettings;
    private readonly ILogger<ResearchCommandHandler> _logger;

    public ResearchCommandHandler(
        IModelClient modelClient,
        ISearchProvider searchProvider,
        ITokenCounter tokenCounter,
        IOptions<StateMachineSettings> stateOptions,
        IOptions<SearchSettings> searchOptions,
        ILogger<ResearchCommandHandler> logger)
    {
        _modelClient = modelClient;
        _searchProvider = searchProvider;
        _tokenCounter = tokenCounter;
        _stateSettings = stateOptions.Value;
        _searchSettings = searchOptions.Value;
        _logger = logger;
    }

    public Task<IAsyncEnumerable<ResearchProgressEvent>> Handle(
        ResearchCommand request, CancellationToken ct)
        => Task.FromResult(StreamAsync(request, ct));

    // NOTE: C# forbids `yield` inside a try that has a catch/finally, so the iterator itself is
    // try/catch-free. Risky steps are isolated in Safe* helpers that catch internally and surface
    // errors as tuples; the iterator emits Error events based on those results.
    private async IAsyncEnumerable<ResearchProgressEvent> StreamAsync(
        ResearchCommand request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);

        var modelId = request.ModelId ?? _stateSettings.DefaultModelId;
        var maxSteps = Math.Clamp(request.MaxSteps ?? 3, 1, 8);
        var sources = new List<ResearchSource>();
        var queries = new List<string>();
        var totalPrompt = 0;
        var totalCompletion = 0;

        // 1) Plan
        var (planQueries, planUsage, planError) =
            await SafePlanAsync(request.Question, modelId, request.FocusInstructions, maxSteps, ct);
        if (planError != null)
        {
            yield return new ResearchProgressEvent(ResearchEventType.Error, Error: planError);
            yield return new ResearchProgressEvent(
                ResearchEventType.Report, Report: BuildReport(request.Question, queries, sources, totalPrompt, totalCompletion));
            yield break;
        }

        if (planUsage != null)
        {
            totalPrompt += planUsage.PromptTokens;
            totalCompletion += planUsage.CompletionTokens;
        }

        queries.AddRange(planQueries!);
        yield return new ResearchProgressEvent(
            ResearchEventType.Plan, Queries: queries.ToArray(), Message: $"已规划 {queries.Count} 个检索查询");

        // 2) Search loop (real HTTP via ISearchProvider)
        foreach (var q in queries)
        {
            ct.ThrowIfCancellationRequested();
            yield return new ResearchProgressEvent(ResearchEventType.SearchStart, Query: q, Message: $"检索中：{q}");

            var (result, searchError) = await SafeSearchAsync(q, ct);
            if (searchError != null)
            {
                yield return new ResearchProgressEvent(
                    ResearchEventType.SearchDone, Query: q, SnippetCount: 0, Message: $"检索失败：{searchError}");
                continue;
            }

            foreach (var s in result!.Snippets)
            {
                if (sources.Exists(x => x.Url.Equals(s.Url, StringComparison.OrdinalIgnoreCase)))
                    continue;
                sources.Add(new ResearchSource(s.Title, s.Url, s.Snippet));
            }

            yield return new ResearchProgressEvent(
                ResearchEventType.SearchDone, Query: q, SnippetCount: result.Snippets.Count,
                Message: $"检索完成：{q}（{result.Snippets.Count} 条）");
        }

        // 3) Synthesize (budget-compressed context)
        yield return new ResearchProgressEvent(ResearchEventType.Synthesize, Message: "正在综合报告…");
        var contextText = BuildContext(sources, _stateSettings.MaxSummaryTokens);
        var (answer, sections, synthUsage, synthError) =
            await SafeSynthesizeAsync(request.Question, modelId, contextText, request.FocusInstructions, ct);
        if (synthError != null)
        {
            yield return new ResearchProgressEvent(ResearchEventType.Error, Error: synthError);
            yield return new ResearchProgressEvent(
                ResearchEventType.Report, Report: BuildReport(request.Question, queries, sources, totalPrompt, totalCompletion));
            yield break;
        }

        if (synthUsage != null)
        {
            totalPrompt += synthUsage.PromptTokens;
            totalCompletion += synthUsage.CompletionTokens;
        }

        var report = BuildReport(request.Question, queries, sources, totalPrompt, totalCompletion, answer, sections);
        yield return new ResearchProgressEvent(ResearchEventType.Report, Report: report, Message: "报告完成");
    }

    private async Task<(List<string>? Queries, TokenUsage? Usage, string? Error)> SafePlanAsync(
        string question, string modelId, string? focus, int maxSteps, CancellationToken ct)
    {
        try
        {
            var planned = await PlanQueriesAsync(question, modelId, focus, maxSteps, ct);
            return (planned, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "规划阶段失败 q={Question}", question);
            return (null, null, ex.Message);
        }
    }

    private async Task<(SearchResult? Result, string? Error)> SafeSearchAsync(string query, CancellationToken ct)
    {
        try
        {
            var result = await _searchProvider.SearchAsync(query, _searchSettings.DefaultMaxResults, ct);
            if (!result.Success)
                return (null, result.ErrorMessage ?? "搜索失败");
            return (result, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "搜索异常 q={Query}", query);
            return (null, ex.Message);
        }
    }

    private async Task<(string? Answer, List<ResearchSection>? Sections, TokenUsage? Usage, string? Error)> SafeSynthesizeAsync(
        string question, string modelId, string context, string? focus, CancellationToken ct)
    {
        try
        {
            var (answer, sections, usage) = await SynthesizeAsync(question, modelId, context, focus, ct);
            return (answer, sections, usage, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "综合阶段失败 q={Question}", question);
            return (null, null, null, ex.Message);
        }
    }

    private ResearchReport BuildReport(
        string question, List<string> queries, List<ResearchSource> sources,
        int totalPrompt, int totalCompletion, string? answer = null, List<ResearchSection>? sections = null)
        => new ResearchReport(
            question,
            queries.ToArray(),
            sources.ToArray(),
            answer ?? string.Empty,
            sections?.ToArray() ?? Array.Empty<ResearchSection>(),
            queries.Count,
            new TokenUsage(totalPrompt, totalCompletion),
            DateTime.UtcNow);

    private async Task<List<string>> PlanQueriesAsync(
        string question, string modelId, string? focus, int maxSteps, CancellationToken ct)
    {
        var sys = "You are a research planner. Given a question, output a JSON array of up to " +
                  maxSteps + " distinct web search queries (strings) that would help answer it. " +
                  "Output ONLY the JSON array, no prose, no markdown fences.";
        var user = question;
        if (!string.IsNullOrWhiteSpace(focus))
            user += "\nFocus: " + focus;

        var resp = await _modelClient.ChatAsync(modelId, new List<ChatMessage>
        {
            new(MessageRole.System, sys),
            new(MessageRole.User, user)
        }, ct: ct);

        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(resp.Content);
            if (arr is { Count: > 0 })
                return arr.Where(x => !string.IsNullOrWhiteSpace(x)).Take(maxSteps).ToList();
        }
        catch (JsonException)
        {
            _logger.LogWarning("规划模型未返回合法 JSON 数组，回退为单次查询");
        }

        return new List<string> { question };
    }

    private async Task<(string Answer, List<ResearchSection> Sections, TokenUsage? Usage)> SynthesizeAsync(
        string question, string modelId, string context, string? focus, CancellationToken ct)
    {
        var sys = "You are a research synthesizer. Using the provided web search snippets, write a " +
                  "structured Markdown report that answers the question. Organize the body with " +
                  "level-2 Markdown headings (## Heading). Cite sources by their URL where relevant.";

        var userBuilder = new StringBuilder();
        userBuilder.Append("Question: ").Append(question).Append('\n');
        if (!string.IsNullOrWhiteSpace(focus))
            userBuilder.Append("Focus: ").Append(focus).Append('\n');
        userBuilder.Append("\nWeb search snippets:\n").Append(context).Append("\n\nWrite the report now.");

        var resp = await _modelClient.ChatAsync(modelId, new List<ChatMessage>
        {
            new(MessageRole.System, sys),
            new(MessageRole.User, userBuilder.ToString())
        }, ct: ct);

        var (answer, sections) = ParseReport(resp.Content);
        return (answer, sections, resp.TokenUsage);
    }

    private static (string Answer, List<ResearchSection> Sections) ParseReport(string markdown)
    {
        var lines = markdown.Split('\n');
        var sections = new List<ResearchSection>();
        var intro = new StringBuilder();
        ResearchSection? current = null;
        var body = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.StartsWith("## ") && line.Length > 3)
            {
                if (current is not null)
                    sections.Add(current with { Body = body.ToString().Trim() });
                current = new ResearchSection(line[3..].Trim(), string.Empty);
                body.Clear();
            }
            else if (current is null)
            {
                intro.Append(line).Append('\n');
            }
            else
            {
                body.Append(line).Append('\n');
            }
        }

        if (current is not null)
            sections.Add(current with { Body = body.ToString().Trim() });

        return (intro.ToString().Trim(), sections);
    }

    private string BuildContext(List<ResearchSource> sources, int budgetTokens)
    {
        var sb = new StringBuilder();
        var used = 0;
        foreach (var s in sources)
        {
            var block = $"- [{s.Title}]({s.Url}): {s.Snippet}\n";
            var t = _tokenCounter.CountTokens(block);
            if (used + t > budgetTokens)
                break;
            sb.Append(block);
            used += t;
        }

        return sb.ToString();
    }
}
