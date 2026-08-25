using System.Collections.Generic;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Routing;

/// <summary>
/// F31：租户无 BYO 且平台目录为空时，ModelRouter 必须抛出可操作的
/// <see cref="ModelNotConfiguredException"/>（配置缺口），而非笼统的 AllModelsFailedException。
/// </summary>
public sealed class ModelRouterNotConfiguredTests
{
    private static ModelRouter CreateRouter(
        ITenantModelClientResolver? resolver = null,
        IPlatformModelProvider? platformProvider = null)
    {
        return new ModelRouter(
            Substitute.For<IModelClient>(),
            resolver ?? Substitute.For<ITenantModelClientResolver>(),
            Substitute.For<ITenantProvider>(),
            platformProvider ?? Substitute.For<IPlatformModelProvider>(),
            Substitute.For<ICostController>(),
            Substitute.For<IResiliencePipelineProvider>(),
            Substitute.For<ILogger<ModelRouter>>(),
            Options.Create(new RouterSettings()));
    }

    private static RoutingRequest CreateRequest() =>
        new(Guid.NewGuid(), new List<ChatMessage> { new(MessageRole.User, "hello") });

    [Fact]
    public async Task RouteAsync_NoByoAndEmptyPlatformCatalog_ThrowsModelNotConfigured()
    {
        var resolver = Substitute.For<ITenantModelClientResolver>();
        resolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<TenantModelResolution>());
        var platformProvider = Substitute.For<IPlatformModelProvider>();
        platformProvider.GetCandidates().Returns(new List<ModelCandidate>());
        var router = CreateRouter(resolver, platformProvider);

        var ex = await Assert.ThrowsAsync<ModelNotConfiguredException>(
            () => router.RouteAsync(CreateRequest(), CancellationToken.None));

        // 错误信息必须可操作：指出两条配置路径（BYO / 平台 Key），保留原 SemanticKernelModelClient 的指引价值
        Assert.Contains("未配置任何可用模型", ex.Message);
        Assert.Contains("我的凭据", ex.Message);
    }

    [Fact]
    public async Task RouteAsync_WithPlatformCandidate_DoesNotThrowNotConfigured()
    {
        var resolver = Substitute.For<ITenantModelClientResolver>();
        resolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<TenantModelResolution>());
        var platformProvider = Substitute.For<IPlatformModelProvider>();
        platformProvider.GetCandidates().Returns(new List<ModelCandidate>
        {
            new("deepseek-chat", "deepseek", 100)
        });
        var costController = Substitute.For<ICostController>();
        costController.TryReserve(Arg.Any<ModelCandidate>(), Arg.Any<int>(), Arg.Any<Guid>())
            .Returns(true);
        var pipeline = Substitute.For<IResiliencePipelineProvider>();
        pipeline.ExecuteWithRetryAsync(Arg.Any<Func<CancellationToken, Task<ModelResponse>>>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse("ok", null, "deepseek-chat", "stop"));

        var router = new ModelRouter(
            Substitute.For<IModelClient>(),
            resolver,
            Substitute.For<ITenantProvider>(),
            platformProvider,
            costController,
            pipeline,
            Substitute.For<ILogger<ModelRouter>>(),
            Options.Create(new RouterSettings()));

        var response = await router.RouteAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal("ok", response.Content);
    }
}