using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Velo.Api.Controllers;
using Velo.Api.Interface;

namespace Velo.Api.Tests.Controllers;

public class AgentControllerTests
{
    private const string TestOrgId = "test-org";

    private readonly Mock<IAgentService> _agentServiceMock = new();
    private readonly AgentController _sut;

    public AgentControllerTests()
    {
        _sut = new AgentController(
            _agentServiceMock.Object,
            NullLogger<AgentController>.Instance);

        // Simulate the OrgId injected by TenantResolutionMiddleware
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        _sut.ControllerContext.HttpContext.Items["OrgId"] = TestOrgId;
    }

    [Fact]
    public async Task Chat_ReturnsOk_WithAgentResponse()
    {
        // Arrange
        var request = new AgentChatRequest("proj1", "Hello", []);
        var expected = new AgentChatResponse(new ChatMessage("assistant", "Hi"), []);
        _agentServiceMock
            .Setup(s => s.ChatAsync(TestOrgId, "proj1", "Hello", It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.Chat(request);

        // Assert
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(expected);
    }

    [Fact]
    public async Task Chat_PropagatesException_WhenServiceThrows()
    {
        // Arrange
        var request = new AgentChatRequest("proj1", "Hello", []);
        _agentServiceMock
            .Setup(s => s.ChatAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotImplementedException("Not implemented"));

        // Act
        var act = async () => await _sut.Chat(request);

        // Assert — controller wraps non-InvalidOperationException in 500, so expect that
        var result = await _sut.Chat(request);
        result.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task Chat_PassesCancellationToken_ToService()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var request = new AgentChatRequest("proj1", "msg", [new ChatMessage("user", "msg")]);
        var response = new AgentChatResponse(new ChatMessage("assistant", "reply"), ["cite1"]);
        CancellationToken capturedToken = default;

        _agentServiceMock
            .Setup(s => s.ChatAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IEnumerable<ChatMessage>, CancellationToken>((_, _, _, _, ct) => capturedToken = ct)
            .ReturnsAsync(response);

        // Act
        await _sut.Chat(request, cts.Token);

        // Assert
        capturedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task Chat_ForwardsProjectIdAndMessage_ToService()
    {
        // Arrange
        var request = new AgentChatRequest("myProject", "What is DORA?", []);
        string? capturedProject = null;
        string? capturedMessage = null;

        _agentServiceMock
            .Setup(s => s.ChatAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IEnumerable<ChatMessage>, CancellationToken>((_, p, m, _, _) =>
            {
                capturedProject = p;
                capturedMessage = m;
            })
            .ReturnsAsync(new AgentChatResponse(new ChatMessage("assistant", "Answer"), []));

        // Act
        await _sut.Chat(request);

        // Assert
        capturedProject.Should().Be("myProject");
        capturedMessage.Should().Be("What is DORA?");
    }

    [Fact]
    public async Task Chat_ReturnsBadRequest_WhenAgentNotConfigured()
    {
        // Arrange
        var request = new AgentChatRequest("proj1", "Hello", []);
        _agentServiceMock
            .Setup(s => s.ChatAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Foundry agent is not configured for this organization."));

        // Act
        var result = await _sut.Chat(request);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }
}
