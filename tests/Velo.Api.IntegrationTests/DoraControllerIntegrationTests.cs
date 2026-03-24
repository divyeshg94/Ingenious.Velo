using System.Net;
using FluentAssertions;

namespace Velo.Api.IntegrationTests;

/// <summary>
/// Integration tests for the DoraController endpoints.
/// </summary>
public class DoraControllerIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public DoraControllerIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    // ── GET /api/dora/latest ──────────────────────────────────────────────────

    [Fact]
    public async Task GetLatest_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/dora/latest?projectId=myproject");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetLatest_Returns400_WhenProjectIdMissing()
    {
        var client = _factory.CreateAuthenticatedClient($"dora-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/dora/latest");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetLatest_Returns400_WhenProjectIdBlank()
    {
        var client = _factory.CreateAuthenticatedClient($"dora-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/dora/latest?projectId=");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetLatest_Returns200_WithGatheringStatus_WhenNoMetricsAndNoAdoToken()
    {
        var client = _factory.CreateAuthenticatedClient($"dora-gathering-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/dora/latest?projectId=myproject");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("gathering");
    }

    [Fact]
    public async Task GetLatest_Returns200_WithSyncingStatus_WhenNoMetricsAndAdoTokenPresent()
    {
        var client = _factory.CreateAuthenticatedClient($"dora-syncing-{Guid.NewGuid()}");
        client.DefaultRequestHeaders.Add("X-Ado-Access-Token", "fake-ado-token");

        var response = await client.GetAsync("/api/dora/latest?projectId=myproject");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("syncing");
    }

    // ── GET /api/dora/history ─────────────────────────────────────────────────

    [Fact]
    public async Task GetHistory_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/dora/history?projectId=myproject");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetHistory_Returns400_WhenProjectIdMissing()
    {
        var client = _factory.CreateAuthenticatedClient($"dora-hist-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/dora/history");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetHistory_Returns400_WhenDaysIsZero()
    {
        var client = _factory.CreateAuthenticatedClient($"dora-hist-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/dora/history?projectId=myproject&days=0");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetHistory_Returns400_WhenDaysExceeds365()
    {
        var client = _factory.CreateAuthenticatedClient($"dora-hist-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/dora/history?projectId=myproject&days=366");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetHistory_Returns200_WithEmptyArray_WhenNoData()
    {
        var client = _factory.CreateAuthenticatedClient($"dora-hist-empty-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/dora/history?projectId=myproject&days=30");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be("[]");
    }

    [Fact]
    public async Task GetHistory_Returns200_WithDays1_BoundaryAccepted()
    {
        var client = _factory.CreateAuthenticatedClient($"dora-hist-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/dora/history?projectId=myproject&days=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHistory_Returns200_WithDays365_BoundaryAccepted()
    {
        var client = _factory.CreateAuthenticatedClient($"dora-hist-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/dora/history?projectId=myproject&days=365");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
