using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Velo.Api.IntegrationTests;

/// <summary>
/// Integration tests for the OrgsController endpoints.
/// Covers authentication enforcement, happy paths, and error responses.
/// </summary>
public class OrgsControllerIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public OrgsControllerIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    // ── GET /api/orgs/me ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetMe_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/orgs/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_Returns200_WithValidToken()
    {
        var client = _factory.CreateAuthenticatedClient("org-me-test");
        var response = await client.GetAsync("/api/orgs/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMe_AutoCreatesOrg_OnFirstAccess()
    {
        var orgId = $"auto-{Guid.NewGuid()}";
        var client = _factory.CreateAuthenticatedClient(orgId);

        var response = await client.GetAsync("/api/orgs/me");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain(orgId);
    }

    [Fact]
    public async Task GetMe_ReturnsExistingOrg_OnSubsequentAccess()
    {
        var orgId = $"existing-{Guid.NewGuid()}";
        var client = _factory.CreateAuthenticatedClient(orgId);

        // First access creates the org
        await client.GetAsync("/api/orgs/me");
        // Second access returns it
        var response = await client.GetAsync("/api/orgs/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET /api/orgs/projects ────────────────────────────────────────────────

    [Fact]
    public async Task GetProjects_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/orgs/projects");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetProjects_Returns200_WithValidToken()
    {
        var client = _factory.CreateAuthenticatedClient($"proj-org-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/orgs/projects");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetProjects_ReturnsEmptyArray_WhenNoRunsIngested()
    {
        var client = _factory.CreateAuthenticatedClient($"empty-org-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/orgs/projects");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be("[]");
    }

    // ── POST /api/orgs/connect ────────────────────────────────────────────────

    [Fact]
    public async Task Connect_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/orgs/connect",
            new { OrgUrl = "https://dev.azure.com/testorg" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Connect_Returns200_WithValidRequest()
    {
        var client = _factory.CreateAuthenticatedClient($"connect-{Guid.NewGuid()}");
        var response = await client.PostAsJsonAsync("/api/orgs/connect",
            new { OrgUrl = "https://dev.azure.com/myorg" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Connect_Returns400_WhenOrgUrlMissing()
    {
        var client = _factory.CreateAuthenticatedClient($"connect-{Guid.NewGuid()}");
        var response = await client.PostAsJsonAsync("/api/orgs/connect",
            new { OrgUrl = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Connect_ReturnsAutoSyncFalse_WhenNoAdoToken()
    {
        var client = _factory.CreateAuthenticatedClient($"connect-nosync-{Guid.NewGuid()}");
        var response = await client.PostAsJsonAsync("/api/orgs/connect",
            new { OrgUrl = "https://dev.azure.com/myorg" });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"autoSyncTriggered\":false");
    }

    // ── POST /api/orgs/update ─────────────────────────────────────────────────

    [Fact]
    public async Task Update_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/orgs/update",
            new { OrgUrl = "https://dev.azure.com/testorg" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Update_Returns404_WhenOrgNotFound()
    {
        var client = _factory.CreateAuthenticatedClient($"update-notfound-{Guid.NewGuid()}");
        var response = await client.PostAsJsonAsync("/api/orgs/update",
            new { OrgUrl = "https://dev.azure.com/myorg" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_Returns400_WhenOrgUrlMissing()
    {
        var client = _factory.CreateAuthenticatedClient($"update-{Guid.NewGuid()}");
        var response = await client.PostAsJsonAsync("/api/orgs/update",
            new { OrgUrl = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_Returns200_WhenOrgExists()
    {
        var orgId = $"update-exists-{Guid.NewGuid()}";
        var client = _factory.CreateAuthenticatedClient(orgId);

        // First: connect to create the org
        await client.PostAsJsonAsync("/api/orgs/connect",
            new { OrgUrl = "https://dev.azure.com/myorg" });

        // Then: update it
        var response = await client.PostAsJsonAsync("/api/orgs/update",
            new { OrgUrl = "https://dev.azure.com/myorg-updated" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
