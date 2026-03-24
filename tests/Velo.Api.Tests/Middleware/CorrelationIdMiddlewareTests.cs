using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Velo.Api.Middleware;

namespace Velo.Api.Tests.Middleware;

public class CorrelationIdMiddlewareTests
{
    private static DefaultHttpContext MakeContext(string? incomingId = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        if (incomingId != null)
            ctx.Request.Headers["X-Correlation-ID"] = incomingId;
        return ctx;
    }

    [Fact]
    public async Task InvokeAsync_GeneratesNewId_WhenHeaderAbsent()
    {
        var ctx = MakeContext();
        string? captured = null;

        var mw = new CorrelationIdMiddleware(
            next: c => { captured = c.Items["CorrelationId"]?.ToString(); return Task.CompletedTask; },
            logger: NullLogger<CorrelationIdMiddleware>.Instance);

        await mw.InvokeAsync(ctx);

        captured.Should().NotBeNullOrEmpty();
        captured!.Length.Should().Be(32); // Guid("N") format
    }

    [Fact]
    public async Task InvokeAsync_UsesExistingId_WhenHeaderPresent()
    {
        var ctx = MakeContext(incomingId: "existing-id-123");
        string? captured = null;

        var mw = new CorrelationIdMiddleware(
            next: c => { captured = c.Items["CorrelationId"]?.ToString(); return Task.CompletedTask; },
            logger: NullLogger<CorrelationIdMiddleware>.Instance);

        await mw.InvokeAsync(ctx);

        captured.Should().Be("existing-id-123");
    }

    [Fact]
    public async Task InvokeAsync_EchoesIdToResponseHeader()
    {
        var ctx = MakeContext(incomingId: "echo-this");

        var mw = new CorrelationIdMiddleware(
            next: _ => Task.CompletedTask,
            logger: NullLogger<CorrelationIdMiddleware>.Instance);

        await mw.InvokeAsync(ctx);

        ctx.Response.Headers["X-Correlation-ID"].ToString().Should().Be("echo-this");
    }

    [Fact]
    public async Task InvokeAsync_StoresIdInHttpContextItems()
    {
        var ctx = MakeContext();

        var mw = new CorrelationIdMiddleware(
            next: _ => Task.CompletedTask,
            logger: NullLogger<CorrelationIdMiddleware>.Instance);

        await mw.InvokeAsync(ctx);

        ctx.Items.Should().ContainKey("CorrelationId");
        ctx.Items["CorrelationId"].Should().NotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_CallsNext()
    {
        bool called = false;
        var ctx = MakeContext();

        var mw = new CorrelationIdMiddleware(
            next: _ => { called = true; return Task.CompletedTask; },
            logger: NullLogger<CorrelationIdMiddleware>.Instance);

        await mw.InvokeAsync(ctx);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_RethrowsException_FromNext()
    {
        var ctx = MakeContext();

        var mw = new CorrelationIdMiddleware(
            next: _ => throw new InvalidOperationException("boom"),
            logger: NullLogger<CorrelationIdMiddleware>.Instance);

        var act = async () => await mw.InvokeAsync(ctx);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task InvokeAsync_IdIsConsistentAcrossRequestLifecycle()
    {
        var ctx = MakeContext(incomingId: "consistent-id");
        string? fromItems = null;
        string? fromResponse = null;

        var mw = new CorrelationIdMiddleware(
            next: c =>
            {
                fromItems = c.Items["CorrelationId"]?.ToString();
                fromResponse = c.Response.Headers["X-Correlation-ID"].ToString();
                return Task.CompletedTask;
            },
            logger: NullLogger<CorrelationIdMiddleware>.Instance);

        await mw.InvokeAsync(ctx);

        fromItems.Should().Be("consistent-id");
        fromResponse.Should().Be("consistent-id");
    }
}
