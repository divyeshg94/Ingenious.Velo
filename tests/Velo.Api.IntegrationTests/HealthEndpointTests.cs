using FluentAssertions;

namespace Velo.Api.IntegrationTests;

/// <summary>
/// Integration tests for the unauthenticated /health and /debug/auth endpoints.
/// These require no JWT token or database state.
/// </summary>
public class HealthEndpointTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    private readonly ApiFactory _factory;

    public HealthEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_Health_Returns200()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_Health_ReturnsHealthyStatus()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("healthy");
    }

    [Fact]
    public async Task Get_DebugAuth_Returns200_WithoutToken()
    {
        var response = await _client.GetAsync("/debug/auth");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_DebugAuth_ReportsNotAuthenticated_WithoutToken()
    {
        var response = await _client.GetAsync("/debug/auth");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("\"authenticated\":false");
    }

    [Fact]
    public async Task Get_DebugAuth_ReportsAuthenticated_WithValidToken()
    {
        // Use the factory's handler — raw HttpClient bypasses the in-process test server.
        var client = _factory.CreateAuthenticatedClient("my-org");

        var response = await client.GetAsync("/debug/auth");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("\"authenticated\":true");
    }

    [Fact]
    public async Task Get_DebugAuth_IncludesCorrelationIdResponseHeader()
    {
        var response = await _client.GetAsync("/debug/auth");

        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        values!.First().Should().NotBeNullOrEmpty();
    }
}
