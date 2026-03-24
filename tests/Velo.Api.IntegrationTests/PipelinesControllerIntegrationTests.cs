using System.Net;
using FluentAssertions;

namespace Velo.Api.IntegrationTests;

/// <summary>
/// Integration tests for the PipelinesController endpoint.
/// </summary>
public class PipelinesControllerIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PipelinesControllerIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetRuns_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/pipelines/runs?projectId=myproject");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetRuns_Returns200_WithValidToken()
    {
        var client = _factory.CreateAuthenticatedClient($"pipe-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/pipelines/runs?projectId=myproject");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRuns_Returns200_WithEmptyArray_WhenNoData()
    {
        var client = _factory.CreateAuthenticatedClient($"pipe-empty-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/pipelines/runs?projectId=myproject");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be("[]");
    }

    [Fact]
    public async Task GetRuns_Returns200_WithDefaultPagination()
    {
        var client = _factory.CreateAuthenticatedClient($"pipe-paging-{Guid.NewGuid()}");
        // No page/pageSize params - should default to page=1, pageSize=50
        var response = await client.GetAsync("/api/pipelines/runs?projectId=myproject");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRuns_Returns200_WithExplicitPagination()
    {
        var client = _factory.CreateAuthenticatedClient($"pipe-paging-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/pipelines/runs?projectId=myproject&page=2&pageSize=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
