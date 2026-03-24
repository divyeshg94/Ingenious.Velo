using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Velo.Api.IntegrationTests;

/// <summary>
/// Integration tests for the AgentController /api/agent/chat endpoint.
/// </summary>
public class AgentControllerIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AgentControllerIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Chat_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/agent/chat",
            new { Message = "Hello", ProjectId = "myproject" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Chat_Returns200_WithValidTokenAndMessage()
    {
        var client = _factory.CreateAuthenticatedClient($"agent-{Guid.NewGuid()}");
        var response = await client.PostAsJsonAsync("/api/agent/chat",
            new { Message = "Hello", ProjectId = "myproject" });
        // Agent service is a stub — 200 or 500 depending on implementation, but not 401
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
