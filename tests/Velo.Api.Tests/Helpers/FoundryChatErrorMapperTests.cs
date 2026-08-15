using Azure;
using FluentAssertions;
using Velo.Api.Helpers;

namespace Velo.Api.Tests.Helpers;

public class FoundryChatErrorMapperTests
{
    private static RequestFailedException Ex(int status, string message = "Foundry error")
        => new(status, message);

    [Fact]
    public void Map_404_WithOpenAiEndpoint_ReturnsOpenAiGuidance()
    {
        var result = FoundryChatErrorMapper.Map(Ex(404), "https://my-resource.openai.azure.com", "gpt-4o");

        result.Should().NotBeNull();
        result!.Message.Should().Contain("openai.azure.com").And.Contain("not Foundry project endpoints");
    }

    [Fact]
    public void Map_404_WithFoundryEndpoint_ReturnsDeploymentNameInMessage()
    {
        var result = FoundryChatErrorMapper.Map(
            Ex(404), "https://hub.services.ai.azure.com/api/projects/proj", "gpt-4o-custom");

        result.Should().NotBeNull();
        result!.Message.Should().Contain("gpt-4o-custom").And.Contain("Resource not found (404)");
    }

    [Fact]
    public void Map_429_ReturnsRateLimitMessage()
    {
        var result = FoundryChatErrorMapper.Map(Ex(429), "https://hub.services.ai.azure.com/api/projects/proj", "gpt-4o");

        result.Should().NotBeNull();
        result!.Message.Should().Contain("rate limit exceeded (429");
    }

    [Theory]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    public void Map_401Or403_ReturnsAuthMessage_WithCorrectLabel(int status, string expectedLabel)
    {
        var result = FoundryChatErrorMapper.Map(Ex(status), "https://hub.services.ai.azure.com/api/projects/proj", "gpt-4o");

        result.Should().NotBeNull();
        result!.Message.Should().Contain($"({status} {expectedLabel})").And.Contain("Azure AI User");
    }

    [Fact]
    public void Map_401Or403_DoesNotLeakRawFoundryMessageToClient()
    {
        var raw = Ex(403, "Internal request id abc-123 for principal xyz lacks data action foo/bar");

        var result = FoundryChatErrorMapper.Map(raw, "https://hub.services.ai.azure.com/api/projects/proj", "gpt-4o");

        result.Should().NotBeNull();
        result!.Message.Should().NotContain("abc-123").And.NotContain("xyz");
        result.InnerException.Should().BeSameAs(raw);
    }

    [Fact]
    public void Map_UnhandledStatus_ReturnsNull()
    {
        var result = FoundryChatErrorMapper.Map(Ex(500), "https://hub.services.ai.azure.com/api/projects/proj", "gpt-4o");

        result.Should().BeNull();
    }
}
