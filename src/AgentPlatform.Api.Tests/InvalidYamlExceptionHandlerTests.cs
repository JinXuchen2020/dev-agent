using System.Net;
using AgentPlatform.Api.Exceptions;
using AgentPlatform.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentPlatform.Api.Tests;

/// <summary>
/// Verifies <see cref="InvalidYamlExceptionHandler"/> maps <see cref="InvalidYamlException"/>
/// to HTTP 400 while leaving other exceptions untouched.
/// </summary>
public sealed class InvalidYamlExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_MapsInvalidYamlException_To400()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var handler = new InvalidYamlExceptionHandler(NullLogger<InvalidYamlExceptionHandler>.Instance);
        var exception = new InvalidYamlException("YamlContent");

        // Act
        var handled = await handler.TryHandleAsync(context, exception, default);

        // Assert
        Assert.True(handled);
        Assert.Equal((int)HttpStatusCode.BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task TryHandleAsync_IgnoresOtherExceptions()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var handler = new InvalidYamlExceptionHandler(NullLogger<InvalidYamlExceptionHandler>.Instance);

        // Act
        var handled = await handler.TryHandleAsync(context, new InvalidOperationException("boom"), default);

        // Assert
        Assert.False(handled);
        Assert.Equal(200, context.Response.StatusCode);
    }
}
