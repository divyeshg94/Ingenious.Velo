using System.Net;
using FluentAssertions;

namespace Velo.Api.IntegrationTests;

/// <summary>
/// Integration tests for the HealthController (team health metrics) endpoints.
/// Note: Route is /api/health to distinguish from the /health diagnostic endpoint.
/// </summary>
public class HealthControllerIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public HealthControllerIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    // ── GET /api/health/team ──────────────────────────────────────────────────

    [Fact]
    public async Task GetTeamHealth_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/health/team?projectId=myproject");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetTeamHealth_Returns400_WhenProjectIdMissing()
    {
        var client = _factory.CreateAuthenticatedClient($"health-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/health/team");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetTeamHealth_Returns200_WhenNoSnapshot_TriggersInlineCompute()
    {
        var client = _factory.CreateAuthenticatedClient($"health-compute-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/health/team?projectId=myproject");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── POST /api/health/recompute ────────────────────────────────────────────

    [Fact]
    public async Task Recompute_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/health/recompute?projectId=myproject", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Recompute_Returns400_WhenProjectIdMissing()
    {
        var client = _factory.CreateAuthenticatedClient($"health-recompute-{Guid.NewGuid()}");
        var response = await client.PostAsync("/api/health/recompute", null);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Recompute_Returns200_WithValidRequest()
    {
        var client = _factory.CreateAuthenticatedClient($"health-recompute-{Guid.NewGuid()}");
        var response = await client.PostAsync("/api/health/recompute?projectId=myproject", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
