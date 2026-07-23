using System.Net;
using AgentPlatform.Api;
using AgentPlatform.Api.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentPlatform.Api.Tests;

/// <summary>
/// Verifies <see cref="UnsupportedContentTypeExceptionHandler"/> maps the
/// unsupported-content exception to HTTP 415 while leaving other exceptions alone.
/// </summary>
public sealed class UnsupportedContentTypeExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_MapsUnsupportedContentTypeException_To415()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var handler = new UnsupportedContentTypeExceptionHandler(
            NullLogger<UnsupportedContentTypeExceptionHandler>.Instance);
        var exception = new UnsupportedContentTypeException("application/zip");

        // Act
        var handled = await handler.TryHandleAsync(context, exception, default);

        // Assert
        Assert.True(handled);
        Assert.Equal((int)HttpStatusCode.UnsupportedMediaType, context.Response.StatusCode);
    }

    [Fact]
    public async Task TryHandleAsync_IgnoresOtherExceptions()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var handler = new UnsupportedContentTypeExceptionHandler(
            NullLogger<UnsupportedContentTypeExceptionHandler>.Instance);

        // Act
        var handled = await handler.TryHandleAsync(context, new InvalidOperationException("boom"), default);

        // Assert
        Assert.False(handled);
        Assert.Equal(200, context.Response.StatusCode);
    }
}
