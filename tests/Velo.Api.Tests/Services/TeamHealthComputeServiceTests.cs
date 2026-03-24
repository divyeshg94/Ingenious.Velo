using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Velo.Api.Services;
using Velo.Shared.Contracts;
using Velo.Shared.Models;

namespace Velo.Api.Tests.Services;

public class TeamHealthComputeServiceTests
{
    private readonly Mock<IMetricsRepository> _repoMock = new();
    private readonly TeamHealthComputeService _sut;

    public TeamHealthComputeServiceTests()
    {
        _sut = new TeamHealthComputeService(_repoMock.Object, NullLogger<TeamHealthComputeService>.Instance);
    }

    private static PipelineRunDto MakeRun(string result, bool isDeploy = false, long durationMs = 1_800_000,
        int pipelineId = 1, string? name = null, DateTimeOffset? start = null)
    {
        var s = start ?? DateTimeOffset.UtcNow.AddDays(-1);
        return new PipelineRunDto
        {
            Id = Guid.NewGuid(), OrgId = "org", ProjectId = "proj",
            AdoPipelineId = pipelineId,
            PipelineName = name ?? (isDeploy ? "deploy-ci" : "ci-build"),
            Result = result, IsDeployment = isDeploy,
            DurationMs = durationMs,
            StartTime = s, FinishTime = s.AddMilliseconds(durationMs),
            RunNumber = Guid.NewGuid().ToString()
        };
    }

    private static PullRequestEventDto MakePr(string status, bool isApproved = true,
        DateTimeOffset? created = null, DateTimeOffset? closed = null)
    {
        var c = created ?? DateTimeOffset.UtcNow.AddDays(-5);
        return new PullRequestEventDto
        {
            Id = Guid.NewGuid(), OrgId = "org", ProjectId = "proj",
            PrId = new Random().Next(1, 9999),
            Status = status, IsApproved = isApproved,
            CreatedAt = c,
            ClosedAt = closed ?? (status is "completed" or "abandoned" ? c.AddHours(4) : null),
            ReviewerCount = isApproved ? 1 : 0,
            IngestedAt = DateTimeOffset.UtcNow
        };
    }

    private void SetupRepo(List<PipelineRunDto> runs, List<PullRequestEventDto>? prs = null)
    {
        _repoMock.Setup(r => r.GetRunsAsync("org", "proj", 1, 500, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(runs);
        _repoMock.Setup(r => r.GetPrEventsAsync("org", "proj", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(prs ?? []);
        _repoMock.Setup(r => r.SaveTeamHealthAsync(It.IsAny<TeamHealthDto>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
    }

    // ── Empty state ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComputeAndSaveAsync_ReturnsAllZeros_WhenNoData()
    {
        SetupRepo([]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.CodingTimeHours.Should().Be(0);
        result.ReviewTimeHours.Should().Be(0);
        result.MergeTimeHours.Should().Be(0);
        result.DeployTimeHours.Should().Be(0);
        result.TestPassRate.Should().Be(0);
        result.PrApprovalRate.Should().Be(0);
        result.FlakyTestRate.Should().Be(0);
        result.DeploymentRiskScore.Should().Be(0);
        _repoMock.Verify(r => r.SaveTeamHealthAsync(It.IsAny<TeamHealthDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── PR-based review time ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReviewTime_UsesPrData_WhenAvailable()
    {
        var prs = new List<PullRequestEventDto>
        {
            MakePr("completed",
                created: DateTimeOffset.UtcNow.AddDays(-3),
                closed:  DateTimeOffset.UtcNow.AddDays(-3).AddHours(6))
        };
        SetupRepo([MakeRun("succeeded")], prs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ReviewTimeHours.Should().BeApproximately(6.0, 0.1);
    }

    [Fact]
    public async Task ReviewTime_FallsBackToCiProxy_WhenNoPrData()
    {
        SetupRepo([MakeRun("succeeded", isDeploy: false, durationMs: 3_600_000)]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ReviewTimeHours.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public async Task MergeTime_EqualsPrReviewTime_WhenPrDataExists()
    {
        var prs = new List<PullRequestEventDto>
        {
            MakePr("completed",
                created: DateTimeOffset.UtcNow.AddDays(-2),
                closed:  DateTimeOffset.UtcNow.AddDays(-2).AddHours(8))
        };
        SetupRepo([MakeRun("succeeded")], prs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.MergeTimeHours.Should().Be(result.ReviewTimeHours);
    }

    [Fact]
    public async Task ReviewTimeIsZero_WhenOnlyActivePrs()
    {
        var prs = new List<PullRequestEventDto> { MakePr("active", closed: null) };
        SetupRepo([], prs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ReviewTimeHours.Should().Be(0);
    }

    [Fact]
    public async Task ReviewTime_ExcludesOutliersOver30Days()
    {
        var prs = new List<PullRequestEventDto>
        {
            MakePr("completed",
                created: DateTimeOffset.UtcNow.AddDays(-5),
                closed:  DateTimeOffset.UtcNow.AddDays(-5).AddHours(8)),       // normal
            MakePr("completed",
                created: DateTimeOffset.UtcNow.AddDays(-50),
                closed:  DateTimeOffset.UtcNow.AddDays(-5)),                   // 45-day outlier
        };
        SetupRepo([], prs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ReviewTimeHours.Should().BeApproximately(8.0, 0.1);
    }

    // ── PR Approval Rate ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PrApprovalRate_Is50Percent_When2of4Approved()
    {
        var prs = new List<PullRequestEventDto>
        {
            MakePr("completed", isApproved: true),
            MakePr("completed", isApproved: true),
            MakePr("completed", isApproved: false),
            MakePr("completed", isApproved: false),
        };
        SetupRepo([], prs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.PrApprovalRate.Should().BeApproximately(50.0, 0.1);
    }

    [Fact]
    public async Task PrApprovalRate_Is100_WhenAllApproved()
    {
        var prs = new List<PullRequestEventDto>
        {
            MakePr("completed", isApproved: true),
            MakePr("abandoned", isApproved: true),
        };
        SetupRepo([], prs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.PrApprovalRate.Should().Be(100.0);
    }

    [Fact]
    public async Task PrApprovalRate_IsZero_WhenNoClosedPrs()
    {
        SetupRepo([], [MakePr("active", closed: null)]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.PrApprovalRate.Should().Be(0);
    }

    [Fact]
    public async Task PrApprovalRate_FallsBackToTestPassRate_WhenNoPrData()
    {
        // 2 succeeded, 0 failed => pass rate 100%
        SetupRepo([MakeRun("succeeded"), MakeRun("succeeded")]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.PrApprovalRate.Should().Be(result.TestPassRate);
    }

    // ── Test Pass Rate ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TestPassRate_Is66Percent_With2SuccessAnd1Failure()
    {
        SetupRepo([MakeRun("succeeded"), MakeRun("succeeded"), MakeRun("failed")]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.TestPassRate.Should().BeApproximately(66.67, 0.1);
    }

    [Fact]
    public async Task TestPassRate_Is100_WhenAllSucceeded()
    {
        SetupRepo([MakeRun("succeeded"), MakeRun("succeeded")]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.TestPassRate.Should().Be(100);
    }

    [Fact]
    public async Task TestPassRate_ExcludesCancelledRuns()
    {
        SetupRepo([MakeRun("succeeded"), MakeRun("canceled")]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        // canceled is not in the concluded bucket → only 1 run, 1 succeeded = 100%
        result.TestPassRate.Should().Be(100);
    }

    // ── Coding Time ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CodingTime_IsAvgGapBetweenConsecutiveRuns()
    {
        var t = DateTimeOffset.UtcNow.AddDays(-5);
        var runs = new List<PipelineRunDto>
        {
            MakeRun("succeeded", start: t),
            MakeRun("succeeded", start: t.AddHours(2)),
            MakeRun("succeeded", start: t.AddHours(4)),
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.CodingTimeHours.Should().BeApproximately(2.0, 0.1);
    }

    [Fact]
    public async Task CodingTime_IsZero_WhenFewerThan2Runs()
    {
        SetupRepo([MakeRun("succeeded")]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.CodingTimeHours.Should().Be(0);
    }

    [Fact]
    public async Task CodingTime_IgnoresGapsOver24Hours()
    {
        var t = DateTimeOffset.UtcNow.AddDays(-10);
        var runs = new List<PipelineRunDto>
        {
            MakeRun("succeeded", start: t),
            MakeRun("succeeded", start: t.AddHours(2)),     // 2h gap ✓
            MakeRun("succeeded", start: t.AddHours(30)),    // 28h gap → excluded
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.CodingTimeHours.Should().BeApproximately(2.0, 0.1);
    }

    // ── Deploy Time ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeployTime_IsAvgDurationOfDeployRuns()
    {
        var runs = new List<PipelineRunDto>
        {
            MakeRun("succeeded", isDeploy: true, durationMs: 7_200_000),
            MakeRun("succeeded", isDeploy: true, durationMs: 3_600_000),
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.DeployTimeHours.Should().BeApproximately(1.5, 0.01);
    }

    [Fact]
    public async Task DeployTime_IsZero_WhenNoDeployRuns()
    {
        SetupRepo([MakeRun("succeeded", isDeploy: false)]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.DeployTimeHours.Should().Be(0);
    }

    // ── Flaky Test Rate ───────────────────────────────────────────────────────────

    [Fact]
    public async Task FlakyTestRate_IsZero_WhenFewerThan3RunsPerPipeline()
    {
        SetupRepo([MakeRun("succeeded", pipelineId: 1), MakeRun("failed", pipelineId: 1)]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.FlakyTestRate.Should().Be(0);
    }

    [Fact]
    public async Task FlakyTestRate_IsPositive_WhenPipelineAlternatesResults()
    {
        var runs = Enumerable.Range(0, 6)
            .Select(i => MakeRun(i % 2 == 0 ? "succeeded" : "failed", pipelineId: 1))
            .ToList();
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.FlakyTestRate.Should().Be(100); // 1/1 pipelines is flaky
    }

    [Fact]
    public async Task FlakyTestRate_IsZero_WhenPipelineAlwaysSucceeds()
    {
        var runs = Enumerable.Range(0, 5).Select(_ => MakeRun("succeeded", pipelineId: 1)).ToList();
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.FlakyTestRate.Should().Be(0);
    }

    // ── Deployment Risk Score ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeploymentRiskScore_IsZero_WhenNoFailures_AndFastDeploys()
    {
        var runs = Enumerable.Range(0, 5)
            .Select(_ => MakeRun("succeeded", isDeploy: true, durationMs: 900_000)) // 15 min
            .ToList();
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.DeploymentRiskScore.Should().Be(0);
    }

    [Fact]
    public async Task DeploymentRiskScore_IsPositive_WhenHighFailureRate()
    {
        var runs = Enumerable.Range(0, 5).Select(_ => MakeRun("failed")).ToList();
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.DeploymentRiskScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DeploymentRiskScore_NeverExceeds100()
    {
        var runs = Enumerable.Range(0, 20)
            .Select(i => MakeRun(i % 2 == 0 ? "failed" : "failed", pipelineId: i % 3, isDeploy: true, durationMs: 10_000_000))
            .ToList();
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.DeploymentRiskScore.Should().BeLessOrEqualTo(100);
    }

    // ── Phase 2 placeholders ──────────────────────────────────────────────────────

    [Fact]
    public async Task AveragePrSizeLines_IsZero_Phase2Placeholder()
    {
        SetupRepo([]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.AveragePrSizeLines.Should().Be(0);
    }

    [Fact]
    public async Task PrCommentDensity_IsZero_Phase2Placeholder()
    {
        SetupRepo([]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.PrCommentDensity.Should().Be(0);
    }
}
