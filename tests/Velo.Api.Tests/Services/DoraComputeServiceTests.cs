using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Velo.Api.Services;
using Velo.Shared.Contracts;
using Velo.Shared.Models;

namespace Velo.Api.Tests.Services;

public class DoraComputeServiceTests
{
    private readonly Mock<IMetricsRepository> _repoMock = new();
    private readonly DoraComputeService _sut;

    public DoraComputeServiceTests()
    {
        _sut = new DoraComputeService(_repoMock.Object, NullLogger<DoraComputeService>.Instance);
    }

    private static PipelineRunDto Run(string result, bool isDeploy = false, double durationMs = 3_600_000,
        DateTimeOffset? start = null, string pipeline = "ci-pipeline", int pipelineId = 1)
    {
        var s = start ?? DateTimeOffset.UtcNow.AddDays(-1);
        return new PipelineRunDto
        {
            Id = Guid.NewGuid(), OrgId = "org", ProjectId = "proj",
            PipelineName = isDeploy ? "deploy-pipeline" : pipeline,
            RunNumber = Guid.NewGuid().ToString(),
            Result = result, IsDeployment = isDeploy,
            StartTime = s, FinishTime = s.AddMilliseconds(durationMs),
            DurationMs = (long)durationMs,
            AdoPipelineId = isDeploy ? 2 : pipelineId
        };
    }

    private void SetupRepo(List<PipelineRunDto> runs)
    {
        _repoMock.Setup(r => r.GetRunsAsync("org", "proj", 1, 500, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(runs);
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<DoraMetricsDto>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
    }

    // ── Empty runs ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComputeAndSaveAsync_WithEmptyRuns_ReturnsZeroMetrics()
    {
        SetupRepo([]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.DeploymentFrequency.Should().Be(0);
        result.ChangeFailureRate.Should().Be(0);
        result.MeanTimeToRestoreHours.Should().Be(0);
        result.LeadTimeForChangesHours.Should().Be(0);
        result.ReworkRate.Should().Be(0);
        _repoMock.Verify(r => r.SaveAsync(It.IsAny<DoraMetricsDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Deployment Frequency ──────────────────────────────────────────────────────

    [Fact]
    public async Task ComputeAndSaveAsync_ComputesDeploymentFrequency_FromDeployRuns()
    {
        var runs = Enumerable.Range(0, 30).Select(_ => Run("succeeded", isDeploy: true)).ToList();
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.DeploymentFrequency.Should().Be(1.0); // 30 deploys / 30 days
    }

    [Fact]
    public async Task ComputeAndSaveAsync_FallsBackToSuccessfulRuns_WhenNoDeployments()
    {
        var runs = new List<PipelineRunDto>
        {
            Run("succeeded", isDeploy: false),
            Run("succeeded", isDeploy: false),
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.DeploymentFrequency.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ComputeAndSaveAsync_DeploymentFrequency_ExcludesFailedDeployments()
    {
        var runs = new List<PipelineRunDto>
        {
            Run("succeeded", isDeploy: true),
            Run("failed",    isDeploy: true),
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        // Only 1 succeeded deploy counts
        result.DeploymentFrequency.Should().BeLessThan(0.1);
    }

    // ── Change Failure Rate ───────────────────────────────────────────────────────

    [Fact]
    public async Task ComputeAndSaveAsync_ChangeFailureRate_Is50Percent()
    {
        var runs = new List<PipelineRunDto>
        {
            Run("succeeded"), Run("succeeded"), Run("failed"), Run("failed")
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ChangeFailureRate.Should().Be(50.0);
    }

    [Fact]
    public async Task ComputeAndSaveAsync_ChangeFailureRate_IsZero_WhenAllSucceeded()
    {
        SetupRepo([Run("succeeded"), Run("succeeded")]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ChangeFailureRate.Should().Be(0);
    }

    [Fact]
    public async Task ComputeAndSaveAsync_ChangeFailureRate_Is100Percent_WhenAllFailed()
    {
        SetupRepo([Run("failed"), Run("failed")]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ChangeFailureRate.Should().Be(100.0);
    }

    // ── Lead Time ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComputeAndSaveAsync_LeadTime_AverageDurationOfSuccessfulRuns()
    {
        var runs = new List<PipelineRunDto>
        {
            Run("succeeded", durationMs: 7_200_000),   // 2h
            Run("succeeded", durationMs: 3_600_000),   // 1h
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.LeadTimeForChangesHours.Should().BeApproximately(1.5, 0.01);
    }

    [Fact]
    public async Task ComputeAndSaveAsync_LeadTime_IsZero_WhenNoSuccessfulRunsWithDuration()
    {
        SetupRepo([Run("failed", durationMs: 3_600_000)]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.LeadTimeForChangesHours.Should().Be(0);
    }

    // ── MTTR ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComputeAndSaveAsync_Mttr_ComputedFromFailureToNextSuccess()
    {
        var base1 = DateTimeOffset.UtcNow.AddDays(-5);
        var runs = new List<PipelineRunDto>
        {
            Run("failed",    start: base1,             pipeline: "pipe-a"),
            Run("succeeded", start: base1.AddHours(3), pipeline: "pipe-a"),
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.MeanTimeToRestoreHours.Should().BeApproximately(3.0, 0.1);
    }

    [Fact]
    public async Task ComputeAndSaveAsync_Mttr_IsZero_WhenNoSuccessAfterFailure()
    {
        SetupRepo([Run("failed")]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.MeanTimeToRestoreHours.Should().Be(0);
    }

    [Fact]
    public async Task ComputeAndSaveAsync_Mttr_IgnoresDifferentPipelineNames()
    {
        var base1 = DateTimeOffset.UtcNow.AddDays(-5);
        var runs = new List<PipelineRunDto>
        {
            Run("failed",    start: base1,             pipeline: "pipe-a"),
            Run("succeeded", start: base1.AddHours(2), pipeline: "pipe-b"), // different pipeline
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        // MTTR only matches same pipeline name
        result.MeanTimeToRestoreHours.Should().Be(0);
    }

    // ── Rework Rate ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComputeAndSaveAsync_ReworkRate_IsZero_WhenAllDistinctPipelines()
    {
        var runs = new List<PipelineRunDto>
        {
            Run("succeeded", pipeline: "pipe-1"),
            Run("succeeded", pipeline: "pipe-2"),
            Run("succeeded", pipeline: "pipe-3"),
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ReworkRate.Should().Be(0);
    }

    [Fact]
    public async Task ComputeAndSaveAsync_ReworkRate_IsPositive_WhenPipelineRerun()
    {
        var runs = new List<PipelineRunDto>
        {
            Run("succeeded", pipeline: "pipe-1"),
            Run("succeeded", pipeline: "pipe-1"),   // rerun
            Run("succeeded", pipeline: "pipe-2"),
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ReworkRate.Should().BeGreaterThan(0);
    }

    // ── Ratings ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1.5, "Elite")]   // >= 1/day (1.5/day is Elite)
    [InlineData(0.2, "High")]    // >= 1/week (0.2/day ≈ 1.4/week)
    [InlineData(0.05, "Medium")] // >= 1/month (0.05/day ≈ 1.5/month)
    [InlineData(0.01, "Medium")] // Math.Max(1,...) forces ≥1 run → 1/30 days = Medium
    public async Task DeploymentFrequencyRating_IsCorrect(double frequency, string expected)
    {
        int count = (int)Math.Max(1, Math.Ceiling(frequency * 30));
        var runs = Enumerable.Range(0, count)
            .Select(i => Run("succeeded", isDeploy: true, start: DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(-i)))
            .ToList();
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.DeploymentFrequencyRating.Should().Be(expected);
    }

    [Theory]
    [InlineData(0.5, "Elite")]
    [InlineData(12, "High")]
    [InlineData(100, "Medium")]
    [InlineData(200, "Low")]
    public async Task LeadTimeRating_IsCorrect(double hours, string expected)
    {
        SetupRepo([Run("succeeded", durationMs: hours * 3_600_000)]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.LeadTimeRating.Should().Be(expected);
    }

    [Theory]
    [InlineData(3, "Elite")]
    [InlineData(8, "High")]
    [InlineData(12, "Medium")]
    [InlineData(20, "Low")]
    public async Task ChangeFailureRating_IsCorrect(int failCount, string expected)
    {
        var runs = Enumerable.Range(0, 100 - failCount).Select(_ => Run("succeeded"))
            .Concat(Enumerable.Range(0, failCount).Select(_ => Run("failed")))
            .ToList();
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ChangeFailureRating.Should().Be(expected);
    }

    [Theory]
    [InlineData(0.5, "Elite")]
    [InlineData(12, "High")]
    [InlineData(100, "Medium")]
    [InlineData(200, "Low")]
    public async Task MttrRating_IsCorrect(double hours, string expected)
    {
        var t = DateTimeOffset.UtcNow.AddDays(-5);
        var runs = new List<PipelineRunDto>
        {
            Run("failed",    start: t,                pipeline: "p"),
            Run("succeeded", start: t.AddHours(hours), pipeline: "p"),
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.MttrRating.Should().Be(expected);
    }

    [Theory]
    [InlineData(3, "Elite")]
    [InlineData(8, "High")]
    [InlineData(15, "Medium")]
    [InlineData(25, "Low")]
    public async Task ReworkRateRating_IsCorrect(int reruns, string expected)
    {
        int total = 100;
        var runs = Enumerable.Range(0, total - reruns)
            .Select(i => Run("succeeded", pipeline: $"unique-{i}"))
            .Concat(Enumerable.Range(0, reruns).Select(_ => Run("succeeded", pipeline: "shared")))
            .ToList();
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ReworkRateRating.Should().Be(expected);
    }

    // ── Period boundary ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ComputeAndSaveAsync_ExcludesRunsOlderThan30Days()
    {
        var old = Run("failed", start: DateTimeOffset.UtcNow.AddDays(-40));
        var recent = Run("succeeded");
        SetupRepo([old, recent]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        // Only the succeeded run is in window, so CFR = 0
        result.ChangeFailureRate.Should().Be(0);
    }

    [Fact]
    public async Task ComputeAndSaveAsync_SetsCorrectOrgAndProjectId()
    {
        SetupRepo([]);
        DoraMetricsDto? saved = null;
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<DoraMetricsDto>(), It.IsAny<CancellationToken>()))
                 .Callback<DoraMetricsDto, CancellationToken>((d, _) => saved = d)
                 .Returns(Task.CompletedTask);

        await _sut.ComputeAndSaveAsync("myorg", "myproj", CancellationToken.None);

        saved!.OrgId.Should().Be("myorg");
        saved.ProjectId.Should().Be("myproj");
    }
}
