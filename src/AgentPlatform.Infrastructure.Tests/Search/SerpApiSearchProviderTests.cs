#nullable disable
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Infrastructure.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Search;

public class SerpApiSearchProviderTests
{
    private static SerpApiSearchProvider Provider(
        HttpMessageHandler handler,
        SearchSettings settings = null,
        ITenantCredentialResolver credentialResolver = null,
        ITenantProvider tenantProvider = null,
        IApiKeyEncryptionService encryption = null,
        ICostController costController = null)
    {
        settings ??= new SearchSettings { SerpApiKey = "testkey", TimeoutSeconds = 15 };
        var factory = new StubHttpClientFactory(handler);

        if (credentialResolver is null)
        {
            credentialResolver = Substitute.For<ITenantCredentialResolver>();
            credentialResolver
                .ResolveAsync(Arg.Any<Guid>(), Arg.Any<CredentialCategory>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TenantCredentialSetting>(null));
        }

        if (tenantProvider is null)
        {
            tenantProvider = Substitute.For<ITenantProvider>();
            tenantProvider.GetTenantId().Returns(Guid.NewGuid());
        }

        encryption ??= Substitute.For<IApiKeyEncryptionService>();

        if (costController is null)
        {
            costController = Substitute.For<ICostController>();
            costController.TryRecordSearch(Arg.Any<Guid>()).Returns(true);
        }

        return new SerpApiSearchProvider(
            factory,
            Options.Create(settings),
            credentialResolver,
            tenantProvider,
            costController,
            encryption,
            Substitute.For<ILogger<SerpApiSearchProvider>>());
    }

    [Fact]
    public async Task SearchAsync_Parses_OrganicResults_From_Real_Response()
    {
        var json = "{\"organic_results\":[{\"title\":\"A\",\"link\":\"http://a\",\"snippet\":\"sa\"},{\"title\":\"B\",\"link\":\"http://b\",\"snippet\":\"sb\"}]}";
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var provider = Provider(handler);

        var result = await provider.SearchAsync("climate", 5, default);

        Assert.True(result.Success);
        Assert.Equal(2, result.Snippets.Count);
        Assert.Equal("A", result.Snippets[0].Title);
        Assert.Equal("http://a", result.Snippets[0].Url);
        Assert.Equal("sa", result.Snippets[0].Snippet);
        Assert.NotNull(handler.CapturedRequest);
        var uri = handler.CapturedRequest.RequestUri.ToString();
        Assert.Contains("engine=google", uri);
        Assert.Contains("q=climate", uri);
        Assert.Contains("api_key=testkey", uri);
        Assert.Contains("num=5", uri);
    }

    [Fact]
    public async Task SearchAsync_MissingKey_Returns_Failure_Without_Http()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var provider = Provider(handler, new SearchSettings { SerpApiKey = "" });

        var result = await provider.SearchAsync("x", 5, default);

        Assert.False(result.Success);
        Assert.Contains("未配置", result.ErrorMessage);
        Assert.Null(handler.CapturedRequest);
    }

    [Fact]
    public async Task SearchAsync_Non2xx_Returns_Failure_With_Status()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("upstream") });
        var provider = Provider(handler);

        var result = await provider.SearchAsync("x", 5, default);

        Assert.False(result.Success);
        Assert.Contains("502", result.ErrorMessage);
    }

    [Fact]
    public async Task SearchAsync_Timeout_Returns_Failure()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            Task.Delay(2000).Wait();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var provider = Provider(handler, new SearchSettings { SerpApiKey = "k", TimeoutSeconds = 1 });

        var result = await provider.SearchAsync("x", 5, default);

        Assert.False(result.Success);
        Assert.Contains("超时", result.ErrorMessage);
    }

    [Fact]
    public async Task SearchAsync_TransportError_Returns_Failure()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("no route"));
        var provider = Provider(handler);

        var result = await provider.SearchAsync("x", 5, default);

        Assert.False(result.Success);
        Assert.Contains("搜索请求失败", result.ErrorMessage);
    }

    // ── F13 租户隔离（搜索 · 重点）──
    [Fact]
    public async Task SearchAsync_UsesTenantByoKey_WhenConfigured()
    {
        var tenantId = Guid.NewGuid();
        var cred = new TenantCredentialSetting(
            Guid.NewGuid(), tenantId, CredentialCategory.Search, "SerpApi", "enc", "aaaaaaaa", null, null, true);
        var resolver = Substitute.For<ITenantCredentialResolver>();
        resolver
            .ResolveAsync(tenantId, CredentialCategory.Search, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(cred));
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.GetTenantId().Returns(tenantId);
        var encryption = Substitute.For<IApiKeyEncryptionService>();
        encryption.DecryptKey("enc").Returns("aaa");

        var json = "{\"organic_results\":[]}";
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var provider = Provider(handler,
            new SearchSettings { SerpApiKey = "platformkey", TimeoutSeconds = 15 },
            resolver, tenantProvider, encryption);

        var result = await provider.SearchAsync("x", 5, default);

        Assert.True(result.Success);
        var uri = handler.CapturedRequest.RequestUri.ToString();
        Assert.Contains("api_key=aaa", uri);
        Assert.DoesNotContain("api_key=platformkey", uri);
    }

    [Fact]
    public async Task SearchAsync_ByoKeyBypassesPlatformQuota()
    {
        var tenantId = Guid.NewGuid();
        var cred = new TenantCredentialSetting(
            Guid.NewGuid(), tenantId, CredentialCategory.Search, "SerpApi", "enc", "aaaaaaaa", null, null, true);
        var resolver = Substitute.For<ITenantCredentialResolver>();
        resolver
            .ResolveAsync(tenantId, CredentialCategory.Search, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(cred));
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.GetTenantId().Returns(tenantId);
        var encryption = Substitute.For<IApiKeyEncryptionService>();
        encryption.DecryptKey("enc").Returns("aaa");
        var costController = Substitute.For<ICostController>();
        costController.TryRecordSearch(Arg.Any<Guid>()).Returns(false); // 平台配额已耗尽

        var json = "{\"organic_results\":[]}";
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var provider = Provider(handler,
            new SearchSettings { SerpApiKey = "platformkey", TimeoutSeconds = 15 },
            resolver, tenantProvider, encryption, costController);

        var result = await provider.SearchAsync("x", 5, default);

        // BYO 密钥不受平台配额限制，仍应成功。
        Assert.True(result.Success);
        Assert.Contains("api_key=aaa", handler.CapturedRequest.RequestUri.ToString());
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public HttpRequestMessage CapturedRequest;
        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CapturedRequest = request;
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new HttpClient(_handler);
    }
}
