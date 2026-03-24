using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Velo.Api.Middleware;
using Velo.SQL;

namespace Velo.Api.Tests.Middleware;

public class RateLimitMiddlewareTests : IDisposable
{
    private readonly VeloDbContext _dbContext;

    public RateLimitMiddlewareTests()
    {
        var options = new DbContextOptionsBuilder<VeloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new VeloDbContext(options);
    }

    public void Dispose() => _dbContext.Dispose();

    private static DefaultHttpContext MakeContext(string path, string? orgId = "testorg")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        if (orgId != null)
            ctx.Items["OrgId"] = orgId;
        return ctx;
    }

    private static Dictionary<string, (int tokenCount, DateTime date)> GetCache()
    {
        var field = typeof(RateLimitMiddleware)
            .GetField("TokenBudgetCache",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (Dictionary<string, (int, DateTime)>)field!.GetValue(null)!;
    }

    // ── Non-agent paths pass through ──────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_PassesThrough_ForDoraPath()
    {
        bool called = false;
        var mw = new RateLimitMiddleware(_ => { called = true; return Task.CompletedTask; },
            NullLogger<RateLimitMiddleware>.Instance);

        await mw.InvokeAsync(MakeContext("/api/dora/latest"), _dbContext);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_PassesThrough_ForSyncPath()
    {
        bool called = false;
        var mw = new RateLimitMiddleware(_ => { called = true; return Task.CompletedTask; },
            NullLogger<RateLimitMiddleware>.Instance);

        await mw.InvokeAsync(MakeContext("/api/sync/proj1"), _dbContext);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_PassesThrough_ForWebhookPath()
    {
        bool called = false;
        var mw = new RateLimitMiddleware(_ => { called = true; return Task.CompletedTask; },
            NullLogger<RateLimitMiddleware>.Instance);

        await mw.InvokeAsync(MakeContext("/api/webhook/ado"), _dbContext);

        called.Should().BeTrue();
    }

    // ── Agent path enforcement ────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_Returns401_WhenOrgIdMissing_OnAgentPath()
    {
        var mw = new RateLimitMiddleware(_ => Task.CompletedTask,
            NullLogger<RateLimitMiddleware>.Instance);
        var ctx = MakeContext("/api/agent/chat", orgId: null);

        await mw.InvokeAsync(ctx, _dbContext);

        ctx.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_AllowsRequest_WithinBudget()
    {
        bool called = false;
        var mw = new RateLimitMiddleware(_ => { called = true; return Task.CompletedTask; },
            NullLogger<RateLimitMiddleware>.Instance);

        await mw.InvokeAsync(MakeContext("/api/agent/chat", orgId: Guid.NewGuid().ToString()), _dbContext);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Returns429_WhenBudgetExceeded()
    {
        var orgId = $"exceeded-{Guid.NewGuid()}";
        var cache = GetCache();
        cache[$"{orgId}:{DateTime.UtcNow.Date:yyyy-MM-dd}"] = (50_001, DateTime.UtcNow.Date);

        var mw = new RateLimitMiddleware(_ => Task.CompletedTask,
            NullLogger<RateLimitMiddleware>.Instance);
        var ctx = MakeContext("/api/agent/chat", orgId: orgId);

        await mw.InvokeAsync(ctx, _dbContext);

        ctx.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task InvokeAsync_ResetsCount_WhenCachedDateIsYesterday()
    {
        var orgId = $"stale-{Guid.NewGuid()}";
        var cache = GetCache();
        // Stale entry from yesterday with huge count
        cache[$"{orgId}:{DateTime.UtcNow.Date:yyyy-MM-dd}"] = (99_999, DateTime.UtcNow.Date.AddDays(-1));

        bool called = false;
        var mw = new RateLimitMiddleware(_ => { called = true; return Task.CompletedTask; },
            NullLogger<RateLimitMiddleware>.Instance);

        await mw.InvokeAsync(MakeContext("/api/agent/chat", orgId: orgId), _dbContext);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_CreatesNewCacheEntry_ForFirstRequest()
    {
        var orgId = $"new-{Guid.NewGuid()}";
        var cache = GetCache();
        var key = $"{orgId}:{DateTime.UtcNow.Date:yyyy-MM-dd}";
        cache.Remove(key);

        var mw = new RateLimitMiddleware(_ => Task.CompletedTask,
            NullLogger<RateLimitMiddleware>.Instance);

        await mw.InvokeAsync(MakeContext("/api/agent/chat", orgId: orgId), _dbContext);

        cache.Should().ContainKey(key);
    }

    [Fact]
    public async Task InvokeAsync_AtExactBudgetLimit_DoesNotReturn429()
    {
        var orgId = $"exact-{Guid.NewGuid()}";
        var cache = GetCache();
        // Exactly at limit (49_999 < 50_000)
        cache[$"{orgId}:{DateTime.UtcNow.Date:yyyy-MM-dd}"] = (49_999, DateTime.UtcNow.Date);

        bool called = false;
        var mw = new RateLimitMiddleware(_ => { called = true; return Task.CompletedTask; },
            NullLogger<RateLimitMiddleware>.Instance);

        await mw.InvokeAsync(MakeContext("/api/agent/chat", orgId: orgId), _dbContext);

        called.Should().BeTrue();
    }
}
