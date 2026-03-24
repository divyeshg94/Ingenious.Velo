using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Velo.Api.Controllers;
using Velo.Api.Interface;
using Velo.Api.Services;
using Velo.Shared.Contracts;
using Velo.Shared.Models;

namespace Velo.Api.Tests.Controllers;

public class OrgsControllerTests
{
    private readonly Mock<IMetricsRepository> _repoMock = new();
    private readonly Mock<IProjectService> _projectServiceMock = new();
    private readonly Mock<AdoPipelineIngestService> _ingestMock;
    private readonly OrgsController _sut;

    public OrgsControllerTests()
    {
        _ingestMock = new Mock<AdoPipelineIngestService>(
            _repoMock.Object,
            Mock.Of<IHttpClientFactory>(),
            new DoraComputeService(_repoMock.Object, NullLogger<DoraComputeService>.Instance),
            NullLogger<AdoPipelineIngestService>.Instance);

        _sut = new OrgsController(
            _repoMock.Object,
            _projectServiceMock.Object,
            _ingestMock.Object,
            NullLogger<OrgsController>.Instance);
    }

    private void SetOrgId(string orgId)
    {
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u1")], "test"))
            }
        };
        _sut.ControllerContext.HttpContext.Items["OrgId"] = orgId;
    }

    [Fact]
    public async Task GetCurrentOrg_ReturnsUnauthorized_WhenOrgIdMissing()
    {
        _sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var result = await _sut.GetCurrentOrg(CancellationToken.None);
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GetCurrentOrg_ReturnsExistingOrg_WhenFound()
    {
        SetOrgId("myorg");
        var org = new OrgContextDto { OrgId = "myorg", DisplayName = "My Org" };
        _repoMock.Setup(r => r.GetOrgContextAsync("myorg", It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _repoMock.Setup(r => r.SaveOrgContextAsync(It.IsAny<OrgContextDto>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await _sut.GetCurrentOrg(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(org);
    }

    [Fact]
    public async Task GetCurrentOrg_AutoCreatesDefaultOrg_WhenNotFound()
    {
        SetOrgId("neworg");
        _repoMock.Setup(r => r.GetOrgContextAsync("neworg", It.IsAny<CancellationToken>())).ReturnsAsync((OrgContextDto?)null);
        _repoMock.Setup(r => r.SaveOrgContextAsync(It.IsAny<OrgContextDto>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await _sut.GetCurrentOrg(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<OrgContextDto>().Subject;
        dto.OrgId.Should().Be("neworg");
        dto.DailyTokenBudget.Should().Be(50_000);
    }

    [Fact]
    public async Task GetCurrentOrg_Returns500_OnException()
    {
        SetOrgId("myorg");
        _repoMock.Setup(r => r.GetOrgContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new Exception("DB error"));

        var result = await _sut.GetCurrentOrg(CancellationToken.None);

        var status = result.Result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task GetAvailableProjects_ReturnsUnauthorized_WhenOrgIdMissing()
    {
        _sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var result = await _sut.GetAvailableProjects(CancellationToken.None);
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GetAvailableProjects_ReturnsProjectList()
    {
        SetOrgId("myorg");
        var projects = new[] { "proj1", "proj2" };
        _projectServiceMock.Setup(p => p.GetProjectsAsync("myorg", It.IsAny<CancellationToken>()))
                           .ReturnsAsync(projects);

        var result = await _sut.GetAvailableProjects(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(projects);
    }

    [Fact]
    public async Task GetAvailableProjects_Returns500_OnException()
    {
        SetOrgId("myorg");
        _projectServiceMock.Setup(p => p.GetProjectsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                           .ThrowsAsync(new Exception("DB error"));

        var result = await _sut.GetAvailableProjects(CancellationToken.None);

        var status = result.Result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task ConnectOrganization_ReturnsUnauthorized_WhenOrgIdMissing()
    {
        _sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var result = await _sut.ConnectOrganization(new UpdateOrgRequest("https://dev.azure.com/org"), CancellationToken.None);
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task ConnectOrganization_ReturnsBadRequest_WhenOrgUrlWhitespace()
    {
        SetOrgId("myorg");
        var result = await _sut.ConnectOrganization(new UpdateOrgRequest("  "), CancellationToken.None);
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ConnectOrganization_CreatesNewOrg_WhenNotExists()
    {
        SetOrgId("neworg");
        _repoMock.Setup(r => r.GetOrgContextAsync("neworg", It.IsAny<CancellationToken>())).ReturnsAsync((OrgContextDto?)null);
        _repoMock.Setup(r => r.SaveOrgContextAsync(It.IsAny<OrgContextDto>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await _sut.ConnectOrganization(new UpdateOrgRequest("https://dev.azure.com/neworg"), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        _repoMock.Verify(r => r.SaveOrgContextAsync(It.IsAny<OrgContextDto>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ConnectOrganization_UpdatesExistingOrg()
    {
        SetOrgId("myorg");
        var existing = new OrgContextDto { OrgId = "myorg", OrgUrl = "https://dev.azure.com/myorg", LastSyncedAt = DateTimeOffset.UtcNow };
        _repoMock.Setup(r => r.GetOrgContextAsync("myorg", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _repoMock.Setup(r => r.SaveOrgContextAsync(It.IsAny<OrgContextDto>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await _sut.ConnectOrganization(new UpdateOrgRequest("https://dev.azure.com/myorg", "New Name"), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ConnectOrganization_Returns500_OnException()
    {
        SetOrgId("myorg");
        _repoMock.Setup(r => r.GetOrgContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new Exception("DB error"));

        var result = await _sut.ConnectOrganization(new UpdateOrgRequest("https://dev.azure.com/myorg"), CancellationToken.None);

        var status = result.Result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task UpdateOrganization_ReturnsUnauthorized_WhenOrgIdMissing()
    {
        _sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var result = await _sut.UpdateOrganization(new UpdateOrgRequest("https://dev.azure.com/org"), CancellationToken.None);
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task UpdateOrganization_ReturnsBadRequest_WhenOrgUrlEmpty()
    {
        SetOrgId("myorg");
        var result = await _sut.UpdateOrganization(new UpdateOrgRequest(""), CancellationToken.None);
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateOrganization_ReturnsNotFound_WhenOrgDoesNotExist()
    {
        SetOrgId("myorg");
        _repoMock.Setup(r => r.GetOrgContextAsync("myorg", It.IsAny<CancellationToken>())).ReturnsAsync((OrgContextDto?)null);

        var result = await _sut.UpdateOrganization(new UpdateOrgRequest("https://dev.azure.com/myorg"), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UpdateOrganization_ReturnsOk_WhenOrgExists()
    {
        SetOrgId("myorg");
        var org = new OrgContextDto { OrgId = "myorg", OrgUrl = "https://dev.azure.com/myorg" };
        _repoMock.Setup(r => r.GetOrgContextAsync("myorg", It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _repoMock.Setup(r => r.SaveOrgContextAsync(It.IsAny<OrgContextDto>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await _sut.UpdateOrganization(new UpdateOrgRequest("https://dev.azure.com/myorg", "Updated"), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task UpdateOrganization_Returns500_OnException()
    {
        SetOrgId("myorg");
        _repoMock.Setup(r => r.GetOrgContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new Exception("DB fail"));

        var result = await _sut.UpdateOrganization(new UpdateOrgRequest("https://dev.azure.com/myorg"), CancellationToken.None);

        var status = result.Result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(500);
    }
}
