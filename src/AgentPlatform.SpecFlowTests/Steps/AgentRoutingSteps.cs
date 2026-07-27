using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Linq;
using System.Threading.Tasks;
using TechTalk.SpecFlow;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

[Binding]
public class AgentRoutingSteps
{
    private readonly IModelClient _modelClient = Substitute.For<IModelClient>();
    private CostController _costController;
    private readonly IResiliencePipelineProvider _pipeline = Substitute.For<IResiliencePipelineProvider>();
    private RouterSettings _routerSettings = null!;
    private IModelRouter _router = null!;
    private string _primaryModel = "";
    private string _specifiedModel = "";
    private ModelResponse? _result;
    private Exception? _caughtException;

    public AgentRoutingSteps()
    {
        _routerSettings = new RouterSettings
        {
            Candidates =
            [
                new() { ModelId = "gpt-4o", Provider = "openai", Priority = 100 },
                new() { ModelId = "deepseek", Provider = "deepseek", Priority = 80 },
                new() { ModelId = "qwen", Provider = "qwen", Priority = 60 }
            ]
        };
        var pricingOptions = Substitute.For<IOptions<PricingSettings>>();
        pricingOptions.Value.Returns(new PricingSettings());
        var routerOptions = Substitute.For<IOptions<RouterSettings>>();
        routerOptions.Value.Returns(_routerSettings);
        var searchOptions = Substitute.For<IOptions<SearchSettings>>();
        searchOptions.Value.Returns(new SearchSettings());
        _costController = new CostController(pricingOptions, routerOptions, searchOptions, Substitute.For<ILogger<CostController>>());
    }

    [Given(@"主模型 ""(.*)"" 调用超时")]
    public void Given主模型调用超时(string primaryModel)
    {
        _primaryModel = primaryModel;

        _modelClient
            .ChatAsync(primaryModel, Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns<Task<ModelResponse>>(_ => throw new TimeoutException("Model timeout"));

        _modelClient
            .ChatAsync(Arg.Is<string>(m => m != primaryModel), Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(args =>
            {
                var modelId = args.Arg<string>();
                return Task.FromResult(new ModelResponse(
                    $"Response from {modelId}",
                    new TokenUsage(10, 20),
                    modelId,
                    "stop"));
            });
    }

    [When(@"路由层触发降级策略")]
    public async Task When路由层触发降级策略()
    {
        await ExecuteRouting(_primaryModel);
    }

    [When(@"路由层触发降级策略并指定偏好模型 ""(.*)""")]
    public async Task When路由层触发降级策略并指定偏好模型(string preferredModel)
    {
        await ExecuteRouting(preferredModel);
    }

    private async Task ExecuteRouting(string? preferredModel)
    {
        _pipeline.ExecuteWithRetryAsync(
                Arg.Any<Func<CancellationToken, Task<ModelResponse>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var operation = callInfo.Arg<Func<CancellationToken, Task<ModelResponse>>>();
                return operation(callInfo.Arg<CancellationToken>());
            });

        var routerOptions = Substitute.For<IOptions<RouterSettings>>();
        routerOptions.Value.Returns(_routerSettings);
        var tenantModelResolver = Substitute.For<ITenantModelClientResolver>();
        tenantModelResolver
            .ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TenantModelResolution?>(null));
        var platformModelProvider = Substitute.For<IPlatformModelProvider>();
        platformModelProvider.GetCandidates()
            .Returns(_routerSettings.Candidates
                .Select(c => new ModelCandidate(c.ModelId, c.Provider, c.Priority))
                .ToList());
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.GetTenantId().Returns(Guid.NewGuid());
        var logger = Substitute.For<ILogger<ModelRouter>>();
        _router = new ModelRouter(_modelClient, tenantModelResolver, tenantProvider, platformModelProvider, _costController, _pipeline, logger, routerOptions);

        try
        {
            _result = await _router.RouteAsync(new RoutingRequest(
                Guid.NewGuid(),
                new List<ChatMessage>
                {
                    new(Domain.Enums.MessageRole.User, "Hello")
                },
                preferredModel), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _caughtException = ex;
        }
    }

    [Then(@"应使用备用模型 ""(.*)"" 重试")]
    public void Then应使用备用模型重试(string fallbackModel)
    {
        Assert.NotNull(_result);
        Assert.Equal(fallbackModel, _result.ModelId);
    }

    [Given(@"所有模型调用都抛出 HttpRequestException")]
    public void Given所有模型调用都抛出HttpRequestException()
    {
        _modelClient
            .ChatAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns<Task<ModelResponse>>(_ => throw new HttpRequestException("HTTP request failed"));
    }

    [Given("预算设置为零")]
    public void Given预算设置为零()
    {
        _routerSettings.PerTenantDailyBudget = 0;
        var pricingOptions = Substitute.For<IOptions<PricingSettings>>();
        pricingOptions.Value.Returns(new PricingSettings());
        var routerOptions = Substitute.For<IOptions<RouterSettings>>();
        routerOptions.Value.Returns(_routerSettings);
        var searchOptions = Substitute.For<IOptions<SearchSettings>>();
        searchOptions.Value.Returns(new SearchSettings());
        _costController = new CostController(pricingOptions, routerOptions, searchOptions, Substitute.For<ILogger<CostController>>());
    }

    [Given(@"主模型 ""(.*)"" 抛出 InvalidOperationException")]
    public void Given主模型抛出InvalidOperationException(string primaryModel)
    {
        _primaryModel = primaryModel;

        _modelClient
            .ChatAsync(primaryModel, Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns<Task<ModelResponse>>(_ => throw new InvalidOperationException("Invalid operation"));

        _modelClient
            .ChatAsync(Arg.Is<string>(m => m != primaryModel), Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(args =>
            {
                var modelId = args.Arg<string>();
                return Task.FromResult(new ModelResponse(
                    $"Response from {modelId}",
                    new TokenUsage(10, 20),
                    modelId,
                    "stop"));
            });
    }

    [Given("候选模型列表为空")]
    public void Given候选模型列表为空()
    {
        _routerSettings.Candidates = [];
    }

    [Given(@"模型 ""(.*)"" 调用返回成功")]
    public void Given模型调用返回成功(string modelId)
    {
        _specifiedModel = modelId;

        _modelClient
            .ChatAsync(modelId, Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(args => Task.FromResult(new ModelResponse(
                $"Response from {modelId}",
                new TokenUsage(10, 20),
                modelId,
                "stop")));
    }

    [Given("其他模型调用返回成功")]
    public void Given其他模型调用返回成功()
    {
        _modelClient
            .ChatAsync(Arg.Is<string>(m => m != _specifiedModel), Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(args =>
            {
                var modelId = args.Arg<string>();
                return Task.FromResult(new ModelResponse(
                    $"Response from {modelId}",
                    new TokenUsage(10, 20),
                    modelId,
                    "stop"));
            });
    }

    [Then(@"应使用模型 ""(.*)"" 响应")]
    public void Then应使用模型响应(string modelId)
    {
        Assert.NotNull(_result);
        Assert.Equal(modelId, _result.ModelId);
    }

    [Then(@"应抛出 (.*)")]
    public void Then应抛出指定异常(string exceptionType)
    {
        Assert.NotNull(_caughtException);
        Assert.Equal(exceptionType, _caughtException.GetType().Name);
    }

    // M20: ModelRouter does not emit domain events. Step removed from feature file.
    // If domain events are added in the future, wire up IDomainEventBus here and assert event emission.
}
