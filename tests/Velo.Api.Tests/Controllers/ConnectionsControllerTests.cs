using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Velo.Api.Controllers;
using Velo.Api.Interface;
using Velo.Shared.Models.Requests;

namespace Velo.Api.Tests.Controllers;

public class ConnectionsControllerTests
{
    private readonly Mock<IConnectionService> _connectionServiceMock = new();
    private readonly ConnectionsController _sut;

    public ConnectionsControllerTests()
    {
        _sut = new ConnectionsController(_connectionServiceMock.Object);
    }

    [Fact]
    public async Task RegisterConnection_ReturnsNoContent_OnSuccess()
    {
        // Arrange
        var config = new ConnectionConfig("https://dev.azure.com/myorg", "pat-token");
        _connectionServiceMock
            .Setup(s => s.RegisterAsync("https://dev.azure.com/myorg", "pat-token", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.RegisterConnection(config);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task RegisterConnection_CallsService_WithCorrectArguments()
    {
        // Arrange
        var config = new ConnectionConfig("https://dev.azure.com/testorg", "my-pat");
        string? capturedUrl = null;
        string? capturedPat = null;

        _connectionServiceMock
            .Setup(s => s.RegisterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((url, pat, _) =>
            {
                capturedUrl = url;
                capturedPat = pat;
            })
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RegisterConnection(config);

        // Assert
        capturedUrl.Should().Be("https://dev.azure.com/testorg");
        capturedPat.Should().Be("my-pat");
    }

    [Fact]
    public async Task RegisterConnection_PropagatesException_WhenServiceThrows()
    {
        // Arrange
        var config = new ConnectionConfig("https://dev.azure.com/myorg", "pat");
        _connectionServiceMock
            .Setup(s => s.RegisterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Org context not set"));

        // Act
        var act = async () => await _sut.RegisterConnection(config);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Org context not set");
    }

    [Fact]
    public async Task RemoveConnection_ReturnsNoContent_OnSuccess()
    {
        // Arrange
        _connectionServiceMock
            .Setup(s => s.RemoveAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.RemoveConnection();

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task RemoveConnection_PropagatesException_WhenServiceThrows()
    {
        // Arrange
        _connectionServiceMock
            .Setup(s => s.RemoveAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Org context not set"));

        // Act
        var act = async () => await _sut.RemoveConnection();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RegisterConnection_PassesCancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var config = new ConnectionConfig("https://dev.azure.com/org", "pat");
        CancellationToken captured = default;

        _connectionServiceMock
            .Setup(s => s.RegisterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, _, ct) => captured = ct)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RegisterConnection(config, cts.Token);

        // Assert
        captured.Should().Be(cts.Token);
    }

    [Fact]
    public async Task RemoveConnection_PassesCancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        CancellationToken captured = default;

        _connectionServiceMock
            .Setup(s => s.RemoveAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(ct => captured = ct)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RemoveConnection(cts.Token);

        // Assert
        captured.Should().Be(cts.Token);
    }
}
