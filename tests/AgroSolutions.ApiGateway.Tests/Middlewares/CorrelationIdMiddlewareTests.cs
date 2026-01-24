using Xunit;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using AgroSolutions.ApiGateway.Middlewares;

namespace AgroSolutions.ApiGateway.Tests.Middlewares;

public class CorrelationIdMiddlewareTests
{
    private readonly Mock<ILogger<CorrelationIdMiddleware>> _loggerMock;
    private readonly Mock<RequestDelegate> _nextMock;

    public CorrelationIdMiddlewareTests()
    {
        _loggerMock = new Mock<ILogger<CorrelationIdMiddleware>>();
        _nextMock = new Mock<RequestDelegate>();
    }

    [Fact]
    public async Task InvokeAsync_ShouldCreateCorrelationId_WhenNotProvided()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_nextMock.Object, _loggerMock.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Items.Should().ContainKey("X-Correlation-Id");
        context.Items["X-Correlation-Id"].Should().NotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_ShouldUseProvidedCorrelationId_WhenPresent()
    {
        // Arrange
        var expectedCorrelationId = "test-correlation-id";
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = expectedCorrelationId;
        
        var middleware = new CorrelationIdMiddleware(_nextMock.Object, _loggerMock.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Items["X-Correlation-Id"].Should().Be(expectedCorrelationId);
    }

    [Fact]
    public async Task InvokeAsync_ShouldCallNextMiddleware()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_nextMock.Object, _loggerMock.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        _nextMock.Verify(next => next(context), Times.Once);
    }
}
