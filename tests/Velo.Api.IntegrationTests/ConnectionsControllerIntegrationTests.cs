using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Velo.Api.IntegrationTests;

/// <summary>
/// Integration tests for the ConnectionsController endpoints.
/// </summary>
public class ConnectionsControllerIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ConnectionsControllerIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    // ── POST /api/connections/register ────────────────────────────────────────

    [Fact]
    public async Task Register_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/connections/register",
            new { OrgUrl = "https://dev.azure.com/testorg", PersonalAccessToken = "mytoken" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_Returns200_WithValidRequest()
    {
        var client = _factory.CreateAuthenticatedClient($"conn-{Guid.NewGuid()}");
        var response = await client.PostAsJsonAsync("/api/connections/register",
            new { OrgUrl = "https://dev.azure.com/testorg", PersonalAccessToken = "mytoken" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── DELETE /api/connections/remove ────────────────────────────────────────

    [Fact]
    public async Task Remove_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.DeleteAsync("/api/connections/remove");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Remove_Returns200_WhenOrgNotFound_NoThrow()
    {
        var client = _factory.CreateAuthenticatedClient($"conn-remove-{Guid.NewGuid()}");
        var response = await client.DeleteAsync("/api/connections/remove");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Remove_Returns200_AfterRegisterAndRemove()
    {
        var orgId = $"conn-full-{Guid.NewGuid()}";
        var client = _factory.CreateAuthenticatedClient(orgId);

        await client.PostAsJsonAsync("/api/connections/register",
            new { OrgUrl = "https://dev.azure.com/testorg", PersonalAccessToken = "mytoken" });

        var response = await client.DeleteAsync("/api/connections/remove");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
