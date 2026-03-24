using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Velo.Api.Controllers;
using Velo.Api.Services;
using Velo.Shared.Contracts;
using Velo.Shared.Models;

namespace Velo.Api.Tests.Controllers;

public class SyncControllerTests
{
    private readonly Mock<IMetricsRepository> _repoMock = new();
    private readonly Mock<AdoPipelineIngestService> _ingestMock;
    private readonly Mock<DoraComputeService> _doraServiceMock;
    private readonly Mock<AdoServiceHookService> _hookServiceMock;
    private readonly SyncController _sut;

    public SyncControllerTests()
    {
        _ingestMock = new Mock<AdoPipelineIngestService>(
            _repoMock.Object,
            Mock.Of<IHttpClientFactory>(),
            new DoraComputeService(_repoMock.Object, NullLogger<DoraComputeService>.Instance),
            NullLogger<AdoPipelineIngestService>.Instance);

        _doraServiceMock = new Mock<DoraComputeService>(
            _repoMock.Object,
            NullLogger<DoraComputeService>.Instance);

        _hookServiceMock = new Mock<AdoServiceHookService>(
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<IConfiguration>(),
            NullLogger<AdoServiceHookService>.Instance);

        _sut = new SyncController(
            _ingestMock.Object,
            _doraServiceMock.Object,
            _hookServiceMock.Object,
            NullLogger<SyncController>.Instance);
    }

    private void SetContext(string orgId, string? adoToken = "test-token")
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u1")], "test"))
        };
        ctx.Items["OrgId"] = orgId;
        if (adoToken != null)
            ctx.Request.Headers["X-Ado-Access-Token"] = adoToken;
        _sut.ControllerContext = new ControllerContext { HttpContext = ctx };
    }

    // ── Sync ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_ReturnsUnauthorized_WhenOrgIdMissing()
    {
        _sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await _sut.Sync("proj1", CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Sync_ReturnsBadRequest_WhenAdoTokenMissing()
    {
        SetContext("myorg", adoToken: null);

        var result = await _sut.Sync("proj1", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Sync_ReturnsOk_OnSuccess()
    {
        SetContext("myorg");
        var metrics = new DoraMetricsDto { OrgId = "myorg" };
        var hookStatus = new WebhookStatusDto { IsRegistered = true };
        var prHookStatus = new WebhookStatusDto { IsRegistered = true };

        _ingestMock.Setup(i => i.IngestAsync("myorg", "proj1", "test-token", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(10);
        _doraServiceMock.Setup(d => d.ComputeAndSaveAsync("myorg", "proj1", It.IsAny<CancellationToken>()))
                        .ReturnsAsync(metrics);
        _hookServiceMock.Setup(h => h.RegisterAsync("myorg", "proj1", "test-token", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(hookStatus);
        _hookServiceMock.Setup(h => h.RegisterPrHookAsync("myorg", "proj1", "test-token", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(prHookStatus);

        var result = await _sut.Sync("proj1", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Sync_StillSucceeds_WhenBuildHookRegistrationThrows()
    {
        SetContext("myorg");
        _ingestMock.Setup(i => i.IngestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(5);
        _doraServiceMock.Setup(d => d.ComputeAndSaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DoraMetricsDto());
        _hookServiceMock.Setup(h => h.RegisterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new Exception("Hook registration failed"));
        _hookServiceMock.Setup(h => h.RegisterPrHookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new WebhookStatusDto { IsRegistered = false });

        var result = await _sut.Sync("proj1", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Sync_StillSucceeds_WhenPrHookRegistrationThrows()
    {
        SetContext("myorg");
        _ingestMock.Setup(i => i.IngestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(5);
        _doraServiceMock.Setup(d => d.ComputeAndSaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DoraMetricsDto());
        _hookServiceMock.Setup(h => h.RegisterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new WebhookStatusDto { IsRegistered = true });
        _hookServiceMock.Setup(h => h.RegisterPrHookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new Exception("PR hook failed"));

        var result = await _sut.Sync("proj1", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Sync_ReturnsBadRequest_OnInvalidOperationException()
    {
        SetContext("myorg");
        _ingestMock.Setup(i => i.IngestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new InvalidOperationException("ADO API error"));

        var result = await _sut.Sync("proj1", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Sync_Returns500_OnUnexpectedException()
    {
        SetContext("myorg");
        _ingestMock.Setup(i => i.IngestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new Exception("unexpected"));

        var result = await _sut.Sync("proj1", CancellationToken.None);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(500);
    }

    // ── GetHookStatus ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHookStatus_ReturnsUnauthorized_WhenOrgIdMissing()
    {
        _sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var result = await _sut.GetHookStatus("proj1", CancellationToken.None);
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GetHookStatus_ReturnsBadRequest_WhenTokenMissing()
    {
        SetContext("myorg", adoToken: null);
        var result = await _sut.GetHookStatus("proj1", CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetHookStatus_ReturnsOk_WithStatus()
    {
        SetContext("myorg");
        var status = new WebhookStatusDto { IsRegistered = true };
        _hookServiceMock.Setup(h => h.GetStatusAsync("myorg", "proj1", "test-token", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(status);

        var result = await _sut.GetHookStatus("proj1", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(status);
    }

    // ── RegisterHook ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterHook_ReturnsOk_WhenRegistered()
    {
        SetContext("myorg");
        _hookServiceMock.Setup(h => h.RegisterAsync("myorg", "proj1", "test-token", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new WebhookStatusDto { IsRegistered = true });

        var result = await _sut.RegisterHook("proj1", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task RegisterHook_Returns422_WhenNotRegistered()
    {
        SetContext("myorg");
        _hookServiceMock.Setup(h => h.RegisterAsync("myorg", "proj1", "test-token", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new WebhookStatusDto { IsRegistered = false, RegistrationError = "Permission denied" });

        var result = await _sut.RegisterHook("proj1", CancellationToken.None);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(422);
    }

    // ── RemoveHook ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveHook_ReturnsNoContent_OnSuccess()
    {
        SetContext("myorg");
        _hookServiceMock.Setup(h => h.RemoveAsync("myorg", "sub123", "test-token", It.IsAny<CancellationToken>()))
                        .ReturnsAsync(true);

        var result = await _sut.RemoveHook("sub123", CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task RemoveHook_Returns500_WhenRemoveFails()
    {
        SetContext("myorg");
        _hookServiceMock.Setup(h => h.RemoveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(false);

        var result = await _sut.RemoveHook("sub123", CancellationToken.None);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(500);
    }

    // ── GetPrHookStatus ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPrHookStatus_ReturnsOk_WithStatus()
    {
        SetContext("myorg");
        var status = new WebhookStatusDto { IsRegistered = false };
        _hookServiceMock.Setup(h => h.GetPrStatusAsync("myorg", "proj1", "test-token", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(status);

        var result = await _sut.GetPrHookStatus("proj1", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(status);
    }

    // ── RegisterPrHook ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterPrHook_ReturnsOk_WhenRegistered()
    {
        SetContext("myorg");
        _hookServiceMock.Setup(h => h.RegisterPrHookAsync("myorg", "proj1", "test-token", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new WebhookStatusDto { IsRegistered = true });

        var result = await _sut.RegisterPrHook("proj1", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task RegisterPrHook_Returns422_WhenNotRegistered()
    {
        SetContext("myorg");
        _hookServiceMock.Setup(h => h.RegisterPrHookAsync("myorg", "proj1", "test-token", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new WebhookStatusDto { IsRegistered = false });

        var result = await _sut.RegisterPrHook("proj1", CancellationToken.None);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(422);
    }
}
