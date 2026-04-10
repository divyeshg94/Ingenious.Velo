using System.Net;
using FluentAssertions;

namespace Velo.Api.IntegrationTests;

/// <summary>
/// Integration tests for the SyncController endpoints.
/// </summary>
public class SyncControllerIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public SyncControllerIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    // ── POST /api/sync/{projectId} ────────────────────────────────────────────

    [Fact]
    public async Task Sync_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/sync/myproject", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sync_Returns400_WhenAdoTokenMissing()
    {
        // Authenticated JWT but no X-Ado-Access-Token header → controller returns 400
        var client = _factory.CreateAuthenticatedClient($"sync-{Guid.NewGuid()}");
        var response = await client.PostAsync("/api/sync/myproject", null);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GET /api/sync/{projectId}/hook-status ─────────────────────────────────

    [Fact]
    public async Task GetHookStatus_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/sync/myproject/hook-status");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetHookStatus_Returns400_WhenAdoTokenMissing()
    {
        // Authenticated JWT but no X-Ado-Access-Token header → controller returns 400
        var client = _factory.CreateAuthenticatedClient($"sync-hook-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/sync/myproject/hook-status");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── POST /api/sync/{projectId}/register-hook ──────────────────────────────

    [Fact]
    public async Task RegisterHook_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/sync/myproject/register-hook", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── DELETE /api/sync/{projectId}/remove-hook ──────────────────────────────

    [Fact]
    public async Task RemoveHook_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.DeleteAsync("/api/sync/myproject/remove-hook");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── GET /api/sync/{projectId}/pr-hook-status ──────────────────────────────

    [Fact]
    public async Task GetPrHookStatus_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/sync/myproject/pr-hook-status");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPrHookStatus_Returns400_WhenAdoTokenMissing()
    {
        // Authenticated JWT but no X-Ado-Access-Token header → controller returns 400
        var client = _factory.CreateAuthenticatedClient($"sync-pr-{Guid.NewGuid()}");
        var response = await client.GetAsync("/api/sync/myproject/pr-hook-status");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── POST /api/sync/{projectId}/register-pr-hook ───────────────────────────

    [Fact]
    public async Task RegisterPrHook_Returns401_WhenNoToken()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/sync/myproject/register-pr-hook", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
