using FluentAssertions;
using Velo.Api.Helpers;

namespace Velo.Api.Tests.Helpers;

public class FoundryChatErrorMapperTests
{
    // Tests exercise the primitives overload (status/message/exception) rather than constructing a
    // real System.ClientModel.ClientResultException — its Status is derived from a PipelineResponse
    // with no public settable constructor, so this is the seam the mapper was split around.
    private static Exception Ex(string message = "Foundry error") => new InvalidOperationException(message);

    [Fact]
    public void Map_404_WithOpenAiEndpoint_ReturnsOpenAiGuidance()
    {
        var result = FoundryChatErrorMapper.Map(404, "Foundry error", Ex(), "https://my-resource.openai.azure.com", "gpt-4o");

        result.Should().NotBeNull();
        result!.Message.Should().Contain("openai.azure.com").And.Contain("not Foundry project endpoints");
    }

    [Fact]
    public void Map_404_WithFoundryEndpoint_ReturnsDeploymentNameInMessage()
    {
        var result = FoundryChatErrorMapper.Map(
            404, "Foundry error", Ex(), "https://hub.services.ai.azure.com/api/projects/proj", "gpt-4o-custom");

        result.Should().NotBeNull();
        result!.Message.Should().Contain("gpt-4o-custom").And.Contain("Resource not found (404)");
    }

    [Fact]
    public void Map_429_ReturnsRateLimitMessage()
    {
        var result = FoundryChatErrorMapper.Map(
            429, "Foundry error", Ex(), "https://hub.services.ai.azure.com/api/projects/proj", "gpt-4o");

        result.Should().NotBeNull();
        result!.Message.Should().Contain("rate limit exceeded (429");
    }

    [Theory]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    public void Map_401Or403_ReturnsAuthMessage_WithCorrectLabel(int status, string expectedLabel)
    {
        var result = FoundryChatErrorMapper.Map(
            status, "Foundry error", Ex(), "https://hub.services.ai.azure.com/api/projects/proj", "gpt-4o");

        result.Should().NotBeNull();
        result!.Message.Should().Contain($"({status} {expectedLabel})").And.Contain("Azure AI User");
    }

    [Fact]
    public void Map_401Or403_DoesNotLeakRawFoundryMessageToClient()
    {
        var raw = Ex("Internal request id abc-123 for principal xyz lacks data action foo/bar");

        var result = FoundryChatErrorMapper.Map(
            403, raw.Message, raw, "https://hub.services.ai.azure.com/api/projects/proj", "gpt-4o");

        result.Should().NotBeNull();
        result!.Message.Should().NotContain("abc-123").And.NotContain("xyz");
        result.InnerException.Should().BeSameAs(raw);
    }

    [Fact]
    public void Map_UnhandledStatus_ReturnsNull()
    {
        var result = FoundryChatErrorMapper.Map(
            500, "Foundry error", Ex(), "https://hub.services.ai.azure.com/api/projects/proj", "gpt-4o");

        result.Should().BeNull();
    }
}
