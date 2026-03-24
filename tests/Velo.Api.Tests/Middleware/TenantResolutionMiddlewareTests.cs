using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Velo.Api.Middleware;
using Velo.SQL;

namespace Velo.Api.Tests.Middleware;

public class TenantResolutionMiddlewareTests : IDisposable
{
    private readonly VeloDbContext _dbContext;
    private const string OrgIdHeader = "X-Azure-DevOps-OrgId";

    public TenantResolutionMiddlewareTests()
    {
        var options = new DbContextOptionsBuilder<VeloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new VeloDbContext(options);
    }

    public void Dispose() => _dbContext.Dispose();

    private static DefaultHttpContext MakeContext(
        bool authenticated = false,
        string? orgIdHeader = null,
        string? tidClaim = null,
        string? oidClaim = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        if (orgIdHeader != null)
            ctx.Request.Headers[OrgIdHeader] = orgIdHeader;

        if (authenticated)
        {
            var claims = new List<Claim>();
            if (tidClaim != null) claims.Add(new Claim("tid", tidClaim));
            if (oidClaim != null) claims.Add(new Claim("oid", oidClaim));

            var identity = new ClaimsIdentity(claims, "TestScheme");
            ctx.User = new ClaimsPrincipal(identity);
        }

        return ctx;
    }

    // ── Unauthenticated pass-through ──────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_CallsNext_WhenUserNotAuthenticated()
    {
        bool called = false;
        var mw = new TenantResolutionMiddleware(
            _ => { called = true; return Task.CompletedTask; },
            NullLogger<TenantResolutionMiddleware>.Instance);

        var ctx = MakeContext(authenticated: false);
        await mw.InvokeAsync(ctx, _dbContext);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_DoesNotSetOrgId_WhenUserNotAuthenticated()
    {
        var mw = new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<TenantResolutionMiddleware>.Instance);

        var ctx = MakeContext(authenticated: false);
        await mw.InvokeAsync(ctx, _dbContext);

        ctx.Items.ContainsKey("OrgId").Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_DoesNotSetDbContextOrgId_WhenUserNotAuthenticated()
    {
        var mw = new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<TenantResolutionMiddleware>.Instance);

        var ctx = MakeContext(authenticated: false);
        await mw.InvokeAsync(ctx, _dbContext);

        _dbContext.CurrentOrgId.Should().BeNull();
    }

    // ── 401 when authenticated but no org resolved ─────────────────────────────

    [Fact]
    public async Task InvokeAsync_Returns401_WhenAuthenticatedButNoOrgId()
    {
        bool called = false;
        var mw = new TenantResolutionMiddleware(
            _ => { called = true; return Task.CompletedTask; },
            NullLogger<TenantResolutionMiddleware>.Instance);

        // Authenticated but no header, no tid, no oid
        var ctx = MakeContext(authenticated: true);
        await mw.InvokeAsync(ctx, _dbContext);

        ctx.Response.StatusCode.Should().Be(401);
        called.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_DoesNotSetOrgId_WhenNoClaimsOrHeader()
    {
        var mw = new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<TenantResolutionMiddleware>.Instance);

        var ctx = MakeContext(authenticated: true);
        await mw.InvokeAsync(ctx, _dbContext);

        ctx.Items.ContainsKey("OrgId").Should().BeFalse();
        _dbContext.CurrentOrgId.Should().BeNull();
    }

    // ── OrgId resolution from header ──────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_SetsOrgIdOnHttpContext_FromHeader()
    {
        var mw = new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<TenantResolutionMiddleware>.Instance);

        var ctx = MakeContext(authenticated: true, orgIdHeader: "my-org");
        // InMemory DB provider throws when the SQL command is issued — catch and inspect side effects
        try { await mw.InvokeAsync(ctx, _dbContext); } catch { }

        ctx.Items["OrgId"].Should().Be("my-org");
    }

    [Fact]
    public async Task InvokeAsync_SetsDbContextCurrentOrgId_FromHeader()
    {
        var mw = new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<TenantResolutionMiddleware>.Instance);

        var ctx = MakeContext(authenticated: true, orgIdHeader: "my-org");
        try { await mw.InvokeAsync(ctx, _dbContext); } catch { }

        _dbContext.CurrentOrgId.Should().Be("my-org");
    }

    // ── OrgId resolution from JWT claims ──────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_SetsOrgId_FromTidClaim_WhenHeaderAbsent()
    {
        var mw = new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<TenantResolutionMiddleware>.Instance);

        var ctx = MakeContext(authenticated: true, tidClaim: "tid-org-123");
        try { await mw.InvokeAsync(ctx, _dbContext); } catch { }

        ctx.Items["OrgId"].Should().Be("tid-org-123");
    }

    [Fact]
    public async Task InvokeAsync_SetsDbContextOrgId_FromTidClaim()
    {
        var mw = new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<TenantResolutionMiddleware>.Instance);

        var ctx = MakeContext(authenticated: true, tidClaim: "tid-org-123");
        try { await mw.InvokeAsync(ctx, _dbContext); } catch { }

        _dbContext.CurrentOrgId.Should().Be("tid-org-123");
    }

    [Fact]
    public async Task InvokeAsync_SetsOrgId_FromOidClaim_WhenHeaderAndTidAbsent()
    {
        var mw = new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<TenantResolutionMiddleware>.Instance);

        var ctx = MakeContext(authenticated: true, oidClaim: "oid-org-456");
        try { await mw.InvokeAsync(ctx, _dbContext); } catch { }

        ctx.Items["OrgId"].Should().Be("oid-org-456");
    }

    // ── Resolution priority order ─────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_HeaderTakesPriority_OverTidClaim()
    {
        var mw = new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<TenantResolutionMiddleware>.Instance);

        var ctx = MakeContext(authenticated: true, orgIdHeader: "header-org", tidClaim: "tid-org");
        try { await mw.InvokeAsync(ctx, _dbContext); } catch { }

        ctx.Items["OrgId"].Should().Be("header-org");
    }

    [Fact]
    public async Task InvokeAsync_HeaderTakesPriority_OverOidClaim()
    {
        var mw = new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<TenantResolutionMiddleware>.Instance);

        var ctx = MakeContext(authenticated: true, orgIdHeader: "header-org", oidClaim: "oid-org");
        try { await mw.InvokeAsync(ctx, _dbContext); } catch { }

        ctx.Items["OrgId"].Should().Be("header-org");
    }

    [Fact]
    public async Task InvokeAsync_TidTakesPriority_OverOidClaim()
    {
        var mw = new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<TenantResolutionMiddleware>.Instance);

        var ctx = MakeContext(authenticated: true, tidClaim: "tid-org", oidClaim: "oid-org");
        try { await mw.InvokeAsync(ctx, _dbContext); } catch { }

        ctx.Items["OrgId"].Should().Be("tid-org");
    }
}
