#nullable disable
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Search;

public class SerpApiSearchProviderTests
{
    private static SerpApiSearchProvider Provider(HttpMessageHandler handler, SearchSettings settings = null)
    {
        settings ??= new SearchSettings { SerpApiKey = "testkey", TimeoutSeconds = 15 };
        var factory = new StubHttpClientFactory(handler);
        return new SerpApiSearchProvider(factory, Options.Create(settings), Substitute.For<ILogger<SerpApiSearchProvider>>());
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
