using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Velo.Api.Controllers;
using Velo.Api.Services;
using Velo.Shared.Contracts;
using Velo.Shared.Models;

namespace Velo.Api.Tests.Controllers;

public class HealthControllerTests
{
    private readonly Mock<IMetricsRepository> _repoMock = new();
    private readonly Mock<ITeamHealthComputeService> _healthServiceMock = new();
    private readonly HealthController _sut;

    public HealthControllerTests()
    {
        _sut = new HealthController(
            _repoMock.Object,
            _healthServiceMock.Object,
            NullLogger<HealthController>.Instance);
    }

    private void SetOrgId(string orgId)
    {
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user1")], "test"))
            }
        };
        _sut.ControllerContext.HttpContext.Items["OrgId"] = orgId;
    }

    // ── GetTeamHealth ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTeamHealth_ReturnsUnauthorized_WhenOrgIdMissing()
    {
        _sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await _sut.GetTeamHealth("proj1");

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GetTeamHealth_ReturnsBadRequest_WhenProjectIdEmpty()
    {
        SetOrgId("myorg");

        var result = await _sut.GetTeamHealth("");

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetTeamHealth_ReturnsOk_WithExistingSnapshot()
    {
        SetOrgId("myorg");
        var existing = new TeamHealthDto { OrgId = "myorg", ProjectId = "proj1" };
        _repoMock.Setup(r => r.GetTeamHealthAsync("myorg", "proj1", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(existing);

        var result = await _sut.GetTeamHealth("proj1");

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(existing);
        _healthServiceMock.Verify(h => h.ComputeAndSaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetTeamHealth_ComputesInline_WhenNoSnapshotExists()
    {
        SetOrgId("myorg");
        var computed = new TeamHealthDto { OrgId = "myorg", ProjectId = "proj1", TestPassRate = 90 };
        _repoMock.Setup(r => r.GetTeamHealthAsync("myorg", "proj1", It.IsAny<CancellationToken>()))
                 .ReturnsAsync((TeamHealthDto?)null);
        _healthServiceMock.Setup(h => h.ComputeAndSaveAsync("myorg", "proj1", It.IsAny<CancellationToken>()))
                          .ReturnsAsync(computed);

        var result = await _sut.GetTeamHealth("proj1");

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(computed);
        _healthServiceMock.Verify(h => h.ComputeAndSaveAsync("myorg", "proj1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTeamHealth_Returns500_OnException()
    {
        SetOrgId("myorg");
        _repoMock.Setup(r => r.GetTeamHealthAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new Exception("DB down"));

        var result = await _sut.GetTeamHealth("proj1");

        var status = result.Result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(500);
    }

    // ── Recompute ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Recompute_ReturnsUnauthorized_WhenOrgIdMissing()
    {
        _sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await _sut.Recompute("proj1");

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Recompute_ReturnsBadRequest_WhenProjectIdEmpty()
    {
        SetOrgId("myorg");

        var result = await _sut.Recompute("");

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Recompute_ReturnsOk_WithFreshHealth()
    {
        SetOrgId("myorg");
        var fresh = new TeamHealthDto { OrgId = "myorg", ProjectId = "proj1", PrApprovalRate = 95 };
        _healthServiceMock.Setup(h => h.ComputeAndSaveAsync("myorg", "proj1", It.IsAny<CancellationToken>()))
                          .ReturnsAsync(fresh);

        var result = await _sut.Recompute("proj1");

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(fresh);
    }

    [Fact]
    public async Task Recompute_Returns500_OnException()
    {
        SetOrgId("myorg");
        _healthServiceMock.Setup(h => h.ComputeAndSaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                          .ThrowsAsync(new Exception("Compute failed"));

        var result = await _sut.Recompute("proj1");

        var status = result.Result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(500);
    }
}
