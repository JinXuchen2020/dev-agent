#nullable disable
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Infrastructure.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Tools;

public class NativeToolExecutorTests
{
    private static NativeToolExecutor Executor(HttpMessageHandler handler, int httpTimeout = 15)
    {
        var settings = new SandboxSettings { HttpTimeoutSeconds = httpTimeout, MaxOutputBytes = 65536 };
        var factory = new StubHttpClientFactory(handler);
        return new NativeToolExecutor(Substitute.For<ILogger<NativeToolExecutor>>(), factory, Options.Create(settings));
    }

    private static ToolDefinition Tool(string endpoint, ToolSource source = ToolSource.NativeTool) =>
        new(Guid.NewGuid(), "web_search", "desc", "{}", "handler", Guid.NewGuid(), source, endpoint);

    [Fact]
    public async Task ExecuteAsync_PostsParameters_And_Returns_Success_With_Body()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") });
        var ex = Executor(handler);

        var result = await ex.ExecuteAsync(Tool("http://test/api"), "{\"query\":\"x\"}", default);

        Assert.True(result.Success);
        Assert.Contains("ok", result.Output);
        Assert.NotNull(handler.CapturedRequest);
        Assert.Equal(HttpMethod.Post, handler.CapturedRequest.Method);
        Assert.NotNull(handler.CapturedRequest.Content);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyParameters_Uses_Get_And_NoBody()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("get-ok") });
        var ex = Executor(handler);

        var result = await ex.ExecuteAsync(Tool("http://test/api"), string.Empty, default);

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Get, handler.CapturedRequest.Method);
        Assert.Null(handler.CapturedRequest.Content);
    }

    [Fact]
    public async Task ExecuteAsync_HttpMethodParam_Selects_Get_With_Params()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("get-ok") });
        var ex = Executor(handler);

        var result = await ex.ExecuteAsync(Tool("http://test/api"), "{\"httpMethod\":\"GET\"}", default);

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Get, handler.CapturedRequest.Method);
    }

    [Fact]
    public async Task ExecuteAsync_Non2xx_Returns_Failure_With_Status()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });
        var ex = Executor(handler);

        var result = await ex.ExecuteAsync(Tool("http://test/api"), "{}", default);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("500", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_MissingEndpointUrl_Returns_Failure()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var ex = Executor(handler);

        var result = await ex.ExecuteAsync(Tool(null), "{}", default);

        Assert.False(result.Success);
        Assert.Contains("EndpointUrl", result.ErrorMessage);
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
