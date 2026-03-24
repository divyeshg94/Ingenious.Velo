using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Velo.Api.Controllers;
using Velo.Api.Services;
using Velo.Api.Tests.Helpers;
using Velo.Shared.Contracts;
using Velo.Shared.Models;

namespace Velo.Api.Tests.Controllers;

public class DoraControllerTests
{
    private readonly Mock<IMetricsRepository> _repoMock = new();
    private readonly Mock<AdoPipelineIngestService> _ingestMock;
    private readonly Mock<DoraComputeService> _doraComputeMock;
    private readonly DoraController _sut;

    public DoraControllerTests()
    {
        _ingestMock = new Mock<AdoPipelineIngestService>(
            Mock.Of<IMetricsRepository>(),
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<DoraComputeService>(MockBehavior.Loose),
            NullLogger<AdoPipelineIngestService>.Instance);

        _doraComputeMock = new Mock<DoraComputeService>(
            Mock.Of<IMetricsRepository>(),
            NullLogger<DoraComputeService>.Instance);

        _sut = new DoraController(
            _repoMock.Object,
            _ingestMock.Object,
            _doraComputeMock.Object,
            NullLogger<DoraController>.Instance);
    }

    private void SetOrgId(string orgId) =>
        _sut.ControllerContext = ControllerContextFactory.WithOrgId(orgId);

    private void SetOrgIdAndAdoToken(string orgId, string adoToken)
    {
        var ctx = ControllerContextFactory.WithOrgIdAndHeader(orgId, "X-Ado-Access-Token", adoToken);
        _sut.ControllerContext = ctx;
    }

    // ── GetLatestMetrics ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestMetrics_ReturnsUnauthorized_WhenOrgIdMissing()
    {
        _sut.ControllerContext = ControllerContextFactory.Empty();

        var result = await _sut.GetLatestMetrics("proj1");

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GetLatestMetrics_ReturnsBadRequest_WhenProjectIdEmpty()
    {
        SetOrgId("org1");

        var result = await _sut.GetLatestMetrics("  ");

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetLatestMetrics_ReturnsMetrics_WhenFound()
    {
        SetOrgId("org1");
        var dto = new DoraMetricsDto { OrgId = "org1", ProjectId = "proj1", DeploymentFrequency = 2.5 };
        _repoMock.Setup(r => r.GetLatestAsync("org1", "proj1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _sut.GetLatestMetrics("proj1");

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(dto);
    }

    [Fact]
    public async Task GetLatestMetrics_ReturnsGathering_WhenNoMetricsAndNoToken()
    {
        SetOrgId("org1");
        _repoMock.Setup(r => r.GetLatestAsync("org1", "proj1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DoraMetricsDto?)null);

        var result = await _sut.GetLatestMetrics("proj1");

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value!.ToString().Should().Contain("gathering");
    }

    [Fact]
    public async Task GetLatestMetrics_ReturnsSyncing_WhenNoMetricsButTokenPresent()
    {
        SetOrgIdAndAdoToken("org1", "my-ado-token");
        _repoMock.Setup(r => r.GetLatestAsync("org1", "proj1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DoraMetricsDto?)null);

        var result = await _sut.GetLatestMetrics("proj1");

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value!.ToString().Should().Contain("syncing");
    }

    [Fact]
    public async Task GetLatestMetrics_Returns500_WhenRepositoryThrows()
    {
        SetOrgId("org1");
        _repoMock.Setup(r => r.GetLatestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        var result = await _sut.GetLatestMetrics("proj1");

        result.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task GetLatestMetrics_ReturnsForbid_WhenUnauthorizedAccessExceptionThrown()
    {
        SetOrgId("org1");
        _repoMock.Setup(r => r.GetLatestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        var result = await _sut.GetLatestMetrics("proj1");

        result.Result.Should().BeOfType<ForbidResult>();
    }

    // ── GetMetricsHistory ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetricsHistory_ReturnsUnauthorized_WhenOrgIdMissing()
    {
        _sut.ControllerContext = ControllerContextFactory.Empty();

        var result = await _sut.GetMetricsHistory("proj1");

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GetMetricsHistory_ReturnsBadRequest_WhenProjectIdEmpty()
    {
        SetOrgId("org1");

        var result = await _sut.GetMetricsHistory("  ");

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetMetricsHistory_ReturnsBadRequest_WhenDaysLessThan1()
    {
        SetOrgId("org1");

        var result = await _sut.GetMetricsHistory("proj1", days: 0);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetMetricsHistory_ReturnsBadRequest_WhenDaysGreaterThan365()
    {
        SetOrgId("org1");

        var result = await _sut.GetMetricsHistory("proj1", days: 366);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetMetricsHistory_ReturnsOk_WithMetricsList()
    {
        SetOrgId("org1");
        var list = new List<DoraMetricsDto> { new() { OrgId = "org1" } };
        _repoMock.Setup(r => r.GetHistoryAsync("org1", "proj1", It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _sut.GetMetricsHistory("proj1", 30);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(list);
    }

    [Fact]
    public async Task GetMetricsHistory_Returns500_WhenRepositoryThrows()
    {
        SetOrgId("org1");
        _repoMock.Setup(r => r.GetHistoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        var result = await _sut.GetMetricsHistory("proj1", 30);

        result.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task GetMetricsHistory_AcceptsBoundaryDays_1()
    {
        SetOrgId("org1");
        _repoMock.Setup(r => r.GetHistoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DoraMetricsDto>());

        var result = await _sut.GetMetricsHistory("proj1", days: 1);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMetricsHistory_AcceptsBoundaryDays_365()
    {
        SetOrgId("org1");
        _repoMock.Setup(r => r.GetHistoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DoraMetricsDto>());

        var result = await _sut.GetMetricsHistory("proj1", days: 365);

        result.Result.Should().BeOfType<OkObjectResult>();
    }
}
