#nullable disable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.ProviderDiscovery;

public class ProviderModelDiscoveryTests
{
    private static ProviderModelDiscovery Provider(HttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        return new ProviderModelDiscovery(
            factory,
            Substitute.For<ILogger<ProviderModelDiscovery>>());
    }

    [Fact]
    public async Task DiscoverAsync_OpenAI_DefaultBase_ResolvesModels()
    {
        var json = "{ \"object\":\"list\", \"data\":[ { \"id\":\"gpt-4o\", \"owned_by\":\"openai\" }, { \"id\":\"gpt-4o-mini\" } ] }";
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });

        var models = await Provider(handler).DiscoverAsync("OpenAI", "sk-test", null, default);

        Assert.Equal(2, models.Count);
        Assert.Equal("gpt-4o", models[0].Id);
        Assert.Equal("openai", models[0].OwnedBy);
        Assert.Equal("gpt-4o-mini", models[1].Id);
        Assert.Equal("https://api.openai.com/v1/models", handler.CapturedRequest.RequestUri.ToString());
        Assert.Equal("Bearer", handler.CapturedRequest.Headers.Authorization.Scheme);
        Assert.Equal("sk-test", handler.CapturedRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task DiscoverAsync_DeepSeek_DefaultBase_AppendsV1()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ \"data\":[ { \"id\":\"deepseek-chat\" } ] }") });

        var models = await Provider(handler).DiscoverAsync("DeepSeek", "sk-test", null, default);

        Assert.Single(models);
        Assert.Equal("deepseek-chat", models[0].Id);
        Assert.Equal("https://api.deepseek.com/v1/models", handler.CapturedRequest.RequestUri.ToString());
    }

    [Fact]
    public async Task DiscoverAsync_Custom_UsesProvidedBaseUrl()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ \"data\":[] }") });

        await Provider(handler).DiscoverAsync("Custom", "sk-test", "https://llm.example.com/v1", default);

        Assert.Equal("https://llm.example.com/v1/models", handler.CapturedRequest.RequestUri.ToString());
    }

    [Fact]
    public async Task DiscoverAsync_Vllm_MissingBaseUrl_Throws()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ProviderModelDiscoveryException>(
            () => Provider(handler).DiscoverAsync("VLLM", "sk-test", null, default));

        Assert.Contains("Base URL", ex.Message);
        Assert.Null(handler.CapturedRequest);
    }

    [Fact]
    public async Task DiscoverAsync_UnknownProvider_Throws()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ProviderModelDiscoveryException>(
            () => Provider(handler).DiscoverAsync("Anthropic", "sk-test", null, default));

        Assert.Contains("不支持的 Provider", ex.Message);
    }

    [Fact]
    public async Task DiscoverAsync_EmptyApiKey_Throws()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ProviderModelDiscoveryException>(
            () => Provider(handler).DiscoverAsync("OpenAI", "", null, default));

        Assert.Contains("API Key 不能为空", ex.Message);
    }

    [Fact]
    public async Task DiscoverAsync_Unauthorized_ReturnsChineseReason()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("nope") });

        var ex = await Assert.ThrowsAsync<ProviderModelDiscoveryException>(
            () => Provider(handler).DiscoverAsync("OpenAI", "bad", null, default));

        Assert.Contains("API Key 无效", ex.Message);
    }

    [Fact]
    public async Task DiscoverAsync_NotFound_ReturnsChineseReason()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") });

        var ex = await Assert.ThrowsAsync<ProviderModelDiscoveryException>(
            () => Provider(handler).DiscoverAsync("Custom", "sk-test", "https://bad.example.com/v1", default));

        Assert.Contains("/models", ex.Message);
    }

    [Fact]
    public async Task DiscoverAsync_EmptyData_ReturnsEmptyList_NotThrows()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ \"data\":[] }") });

        var models = await Provider(handler).DiscoverAsync("OpenAI", "sk-test", null, default);

        Assert.Empty(models);
    }

    [Fact]
    public async Task DiscoverAsync_MissingDataField_Tolerated_ReturnsEmpty()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ \"object\":\"list\" }") });

        var models = await Provider(handler).DiscoverAsync("OpenAI", "sk-test", null, default);

        Assert.Empty(models);
    }

    [Fact]
    public async Task DiscoverAsync_MissingOwnedBy_Tolerated()
    {
        var json = "{ \"data\":[ { \"id\":\"my-model\" } ] }";
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });

        var models = await Provider(handler).DiscoverAsync("OpenAI", "sk-test", null, default);

        Assert.Single(models);
        Assert.Equal("my-model", models[0].Id);
        Assert.Null(models[0].OwnedBy);
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
