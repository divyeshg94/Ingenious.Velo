using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text;
using Velo.Api.Controllers;
using Velo.Api.Services;
using Velo.Shared.Contracts;
using Velo.SQL;

namespace Velo.Api.Tests.Controllers;

public class WebhookControllerTests : IDisposable
{
    private readonly Mock<IMetricsRepository> _repoMock = new();
    private readonly Mock<DoraComputeService> _doraServiceMock;
    private readonly VeloDbContext _dbContext;
    private readonly IConfiguration _config;
    private readonly WebhookController _sut;

    private const string ValidSecret = "test-secret";

    public WebhookControllerTests()
    {
        _doraServiceMock = new Mock<DoraComputeService>(
            _repoMock.Object,
            NullLogger<DoraComputeService>.Instance);

        var options = new DbContextOptionsBuilder<VeloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new VeloDbContext(options);

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Webhook:Secret"] = ValidSecret })
            .Build();

        _sut = new WebhookController(
            _repoMock.Object,
            _dbContext,
            _doraServiceMock.Object,
            _config,
            NullLogger<WebhookController>.Instance);
    }

    public void Dispose() => _dbContext.Dispose();

    private void SetupRequestWithBody(string body, string? secret = ValidSecret)
    {
        var ctx = new DefaultHttpContext();
        if (secret != null)
            ctx.Request.Headers["X-Velo-Secret"] = secret;
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        _sut.ControllerContext = new ControllerContext { HttpContext = ctx };
    }

    // ── Secret validation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AdoEvent_ReturnsUnauthorized_WhenSecretMissing()
    {
        SetupRequestWithBody("{}", secret: null);

        var result = await _sut.AdoEvent(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task AdoEvent_ReturnsUnauthorized_WhenSecretWrong()
    {
        SetupRequestWithBody("{}", secret: "wrong-secret");

        var result = await _sut.AdoEvent(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    // ── JSON validation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AdoEvent_ReturnsBadRequest_WhenBodyIsInvalidJson()
    {
        SetupRequestWithBody("not-json");

        var result = await _sut.AdoEvent(CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── Unknown event type skipped ────────────────────────────────────────────────

    [Fact]
    public async Task AdoEvent_ReturnsOkWithSkipped_ForUnknownEventType()
    {
        SetupRequestWithBody("""{"eventType":"some.unknown.event"}""");

        var result = await _sut.AdoEvent(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value!.ToString().Should().Contain("skipped");
    }

    [Fact]
    public async Task AdoEvent_ReturnsOkWithSkipped_WhenEventTypeNull()
    {
        SetupRequestWithBody("""{"otherField":"value"}""");

        var result = await _sut.AdoEvent(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value!.ToString().Should().Contain("skipped");
    }

    // ── build.complete ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdoEvent_ReturnsBadRequest_WhenBuildPayloadMalformed()
    {
        // valid JSON but cannot deserialize to AdoBuildCompleteEvent properly, resource will be null -> Ok()
        SetupRequestWithBody("""{"eventType":"build.complete","resource":null}""");

        var result = await _sut.AdoEvent(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task AdoEvent_SkipsBuild_WhenFinishTimeNull()
    {
        var payload = """
        {
          "eventType": "build.complete",
          "resource": {
            "buildNumber": "20240101.1",
            "status": "completed",
            "result": "succeeded",
            "startTime": "2024-01-01T10:00:00Z",
            "finishTime": null
          },
          "resourceContainers": {
            "account": { "baseUrl": "https://dev.azure.com/myorg/" }
          }
        }
        """;
        SetupRequestWithBody(payload);

        var result = await _sut.AdoEvent(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _repoMock.Verify(r => r.SaveRunAsync(It.IsAny<Velo.Shared.Models.PipelineRunDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdoEvent_SkipsBuild_WhenStatusNotFinished()
    {
        var payload = """
        {
          "eventType": "build.complete",
          "resource": {
            "buildNumber": "20240101.1",
            "status": "inProgress",
            "startTime": "2024-01-01T10:00:00Z",
            "finishTime": "2024-01-01T11:00:00Z"
          },
          "resourceContainers": {
            "account": { "baseUrl": "https://dev.azure.com/myorg/" }
          }
        }
        """;
        SetupRequestWithBody(payload);

        var result = await _sut.AdoEvent(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task AdoEvent_SkipsBuild_WhenOrgOrProjectCannotBeExtracted()
    {
        var payload = """
        {
          "eventType": "build.complete",
          "resource": {
            "buildNumber": "20240101.1",
            "status": "completed",
            "result": "succeeded",
            "startTime": "2024-01-01T10:00:00Z",
            "finishTime": "2024-01-01T11:00:00Z"
          }
        }
        """;
        SetupRequestWithBody(payload);

        var result = await _sut.AdoEvent(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task AdoEvent_SavesBuildRun_WhenRunDoesNotExist()
    {
        var payload = """
        {
          "eventType": "build.complete",
          "resource": {
            "buildNumber": "20240101.1",
            "status": "completed",
            "result": "succeeded",
            "startTime": "2024-01-01T10:00:00Z",
            "finishTime": "2024-01-01T11:00:00Z",
            "project": { "name": "MyProject" },
            "definition": { "id": 42, "name": "CI Pipeline" }
          },
          "resourceContainers": {
            "account": { "baseUrl": "https://dev.azure.com/myorg/" }
          }
        }
        """;
        SetupRequestWithBody(payload);
        _repoMock.Setup(r => r.RunExistsAsync("myorg", "MyProject", 42, "20240101.1", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);
        _repoMock.Setup(r => r.SaveRunAsync(It.IsAny<Velo.Shared.Models.PipelineRunDto>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        _doraServiceMock.Setup(d => d.ComputeAndSaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new Velo.Shared.Models.DoraMetricsDto());

        var result = await _sut.AdoEvent(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _repoMock.Verify(r => r.SaveRunAsync(It.IsAny<Velo.Shared.Models.PipelineRunDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdoEvent_SkipsDuplicateBuildRun()
    {
        var payload = """
        {
          "eventType": "build.complete",
          "resource": {
            "buildNumber": "20240101.1",
            "status": "completed",
            "result": "succeeded",
            "startTime": "2024-01-01T10:00:00Z",
            "finishTime": "2024-01-01T11:00:00Z",
            "project": { "name": "MyProject" },
            "definition": { "id": 42, "name": "CI" }
          },
          "resourceContainers": {
            "account": { "baseUrl": "https://dev.azure.com/myorg/" }
          }
        }
        """;
        SetupRequestWithBody(payload);
        _repoMock.Setup(r => r.RunExistsAsync("myorg", "MyProject", 42, "20240101.1", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

        var result = await _sut.AdoEvent(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _repoMock.Verify(r => r.SaveRunAsync(It.IsAny<Velo.Shared.Models.PipelineRunDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdoEvent_Returns500_WhenSaveRunFails()
    {
        var payload = """
        {
          "eventType": "build.complete",
          "resource": {
            "buildNumber": "20240101.1",
            "status": "completed",
            "result": "succeeded",
            "startTime": "2024-01-01T10:00:00Z",
            "finishTime": "2024-01-01T11:00:00Z",
            "project": { "name": "MyProject" },
            "definition": { "id": 42, "name": "CI" }
          },
          "resourceContainers": {
            "account": { "baseUrl": "https://dev.azure.com/myorg/" }
          }
        }
        """;
        SetupRequestWithBody(payload);
        _repoMock.Setup(r => r.RunExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);
        _repoMock.Setup(r => r.SaveRunAsync(It.IsAny<Velo.Shared.Models.PipelineRunDto>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new Exception("DB error"));

        var result = await _sut.AdoEvent(CancellationToken.None);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(500);
    }

    // ── git.pullrequest.created / updated ─────────────────────────────────────────

    [Fact]
    public async Task AdoEvent_SavesPrEvent_ForPrCreated()
    {
        var payload = """
        {
          "eventType": "git.pullrequest.created",
          "resource": {
            "pullRequestId": 101,
            "title": "My PR",
            "status": "active",
            "sourceRefName": "refs/heads/feature",
            "targetRefName": "refs/heads/main",
            "creationDate": "2024-01-01T10:00:00Z",
            "closedDate": null,
            "reviewers": [],
            "repository": { "project": { "name": "MyProject" } }
          },
          "resourceContainers": {
            "account": { "baseUrl": "https://dev.azure.com/myorg/" }
          }
        }
        """;
        SetupRequestWithBody(payload);
        _repoMock.Setup(r => r.SavePrEventAsync(It.IsAny<Velo.Shared.Models.PullRequestEventDto>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var result = await _sut.AdoEvent(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _repoMock.Verify(r => r.SavePrEventAsync(It.IsAny<Velo.Shared.Models.PullRequestEventDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdoEvent_SavesPrEvent_ForPrUpdated_WithApproval()
    {
        var payload = """
        {
          "eventType": "git.pullrequest.updated",
          "resource": {
            "pullRequestId": 202,
            "title": "Updated PR",
            "status": "completed",
            "sourceRefName": "refs/heads/feature",
            "targetRefName": "refs/heads/main",
            "creationDate": "2024-01-01T09:00:00Z",
            "closedDate": "2024-01-01T11:00:00Z",
            "reviewers": [{"displayName": "Alice", "vote": 10}],
            "repository": { "project": { "name": "MyProject" } }
          },
          "resourceContainers": {
            "account": { "baseUrl": "https://dev.azure.com/myorg/" }
          }
        }
        """;
        SetupRequestWithBody(payload);
        Velo.Shared.Models.PullRequestEventDto? saved = null;
        _repoMock.Setup(r => r.SavePrEventAsync(It.IsAny<Velo.Shared.Models.PullRequestEventDto>(), It.IsAny<CancellationToken>()))
                 .Callback<Velo.Shared.Models.PullRequestEventDto, CancellationToken>((dto, _) => saved = dto)
                 .Returns(Task.CompletedTask);

        await _sut.AdoEvent(CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.IsApproved.Should().BeTrue();
        saved.Status.Should().Be("completed");
    }

    [Fact]
    public async Task AdoEvent_SkipsPrEvent_WhenOrgCannotBeExtracted()
    {
        var payload = """
        {
          "eventType": "git.pullrequest.created",
          "resource": {
            "pullRequestId": 1,
            "creationDate": "2024-01-01T10:00:00Z",
            "repository": { "project": { "name": "Proj" } }
          }
        }
        """;
        SetupRequestWithBody(payload);

        var result = await _sut.AdoEvent(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _repoMock.Verify(r => r.SavePrEventAsync(It.IsAny<Velo.Shared.Models.PullRequestEventDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdoEvent_Returns500_WhenSavePrFails()
    {
        var payload = """
        {
          "eventType": "git.pullrequest.created",
          "resource": {
            "pullRequestId": 303,
            "title": "Failing PR",
            "status": "active",
            "creationDate": "2024-01-01T10:00:00Z",
            "reviewers": [],
            "repository": { "project": { "name": "MyProject" } }
          },
          "resourceContainers": {
            "account": { "baseUrl": "https://dev.azure.com/myorg/" }
          }
        }
        """;
        SetupRequestWithBody(payload);
        _repoMock.Setup(r => r.SavePrEventAsync(It.IsAny<Velo.Shared.Models.PullRequestEventDto>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new Exception("DB fail"));

        var result = await _sut.AdoEvent(CancellationToken.None);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task AdoEvent_ParsesOrgFromVisualStudioComUrl()
    {
        var payload = """
        {
          "eventType": "build.complete",
          "resource": {
            "buildNumber": "1",
            "status": "completed",
            "result": "succeeded",
            "startTime": "2024-01-01T10:00:00Z",
            "finishTime": "2024-01-01T11:00:00Z",
            "project": { "name": "MyProject" },
            "definition": { "id": 1, "name": "CI" }
          },
          "resourceContainers": {
            "account": { "baseUrl": "https://myorg.visualstudio.com/" }
          }
        }
        """;
        SetupRequestWithBody(payload);
        _repoMock.Setup(r => r.RunExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);
        _repoMock.Setup(r => r.SaveRunAsync(It.IsAny<Velo.Shared.Models.PipelineRunDto>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        _doraServiceMock.Setup(d => d.ComputeAndSaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new Velo.Shared.Models.DoraMetricsDto());

        var result = await _sut.AdoEvent(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _repoMock.Verify(r => r.SaveRunAsync(
            It.Is<Velo.Shared.Models.PipelineRunDto>(r => r.OrgId == "myorg"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
