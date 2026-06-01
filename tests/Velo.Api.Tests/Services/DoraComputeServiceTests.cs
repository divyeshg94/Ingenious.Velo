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
            PipelineName = pipeline,
            RunNumber = Guid.NewGuid().ToString(),
            Result = result, IsDeployment = isDeploy,
            StartTime = s, FinishTime = s.AddMilliseconds(durationMs),
            DurationMs = (long)durationMs,
            AdoPipelineId = pipelineId
        };
    }

    private static WorkItemEventDto WiEvent(string oldState, string newState,
        DateTimeOffset? changedAt = null)
    {
        return new WorkItemEventDto
        {
            Id = Guid.NewGuid(), OrgId = "org", ProjectId = "proj",
            WorkItemId = 1,
            OldState = oldState, NewState = newState,
            ChangedAt = changedAt ?? DateTimeOffset.UtcNow.AddDays(-1),
            IngestedAt = DateTimeOffset.UtcNow,
        };
    }

    private void SetupRepo(List<PipelineRunDto> runs,
        List<WorkItemEventDto>? workItemEvents = null)
    {
        _repoMock.Setup(r => r.GetRunsAsync("org", "proj", 1, 500, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(runs);
        _repoMock.Setup(r => r.GetWorkItemEventsAsync(
                     "org", "proj", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(workItemEvents ?? []);
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

    [Fact]
    public async Task ComputeAndSaveAsync_SetsEstimatedFlag_WhenNoDeploymentTaggedPipelines()
    {
        var runs = new List<PipelineRunDto>
        {
            Run("succeeded", isDeploy: false),
            Run("succeeded", isDeploy: false),
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.IsDeploymentFrequencyEstimated.Should().BeTrue();
    }

    [Fact]
    public async Task ComputeAndSaveAsync_ClearsEstimatedFlag_WhenDeploymentTaggedPipelinesExist()
    {
        var runs = new List<PipelineRunDto>
        {
            Run("succeeded", isDeploy: true),
            Run("succeeded", isDeploy: false),
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.IsDeploymentFrequencyEstimated.Should().BeFalse();
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

    [Fact]
    public async Task ComputeAndSaveAsync_ChangeFailureRate_UsesDeploymentRunsOnly_WhenTagged()
    {
        // 5 failed non-deployment runs should NOT inflate CFR when deployment runs exist
        var runs = new List<PipelineRunDto>
        {
            Run("failed",    isDeploy: false),
            Run("failed",    isDeploy: false),
            Run("failed",    isDeploy: false),
            Run("failed",    isDeploy: false),
            Run("failed",    isDeploy: false),
            Run("succeeded", isDeploy: true),
            Run("succeeded", isDeploy: true),
            Run("failed",    isDeploy: true),   // 1 failed deployment out of 3
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        // CFR = 1 failed deploy / 3 total deploy runs × 100 ≈ 33.3 %
        result.ChangeFailureRate.Should().BeApproximately(33.3, 0.1);
    }

    [Fact]
    public async Task ComputeAndSaveAsync_ChangeFailureRate_FallsBackToAllRuns_WhenNoDeploymentTagged()
    {
        // No deployment-tagged runs; falls back to all runs
        var runs = new List<PipelineRunDto>
        {
            Run("succeeded", isDeploy: false),
            Run("failed",    isDeploy: false),
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ChangeFailureRate.Should().Be(50.0);
        result.IsChangeFailureRateEstimated.Should().BeTrue();
        result.IsDeploymentFrequencyEstimated.Should().BeTrue();
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

    [Fact]
    public async Task ComputeAndSaveAsync_LeadTime_IsAlwaysMarkedApproximate()
    {
        SetupRepo([Run("succeeded", durationMs: 3_600_000)]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.IsLeadTimeApproximate.Should().BeTrue();
    }

    // ── MTTR ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComputeAndSaveAsync_Mttr_ComputedFromDeploymentFailureToNextSuccess()
    {
        var base1 = DateTimeOffset.UtcNow.AddDays(-5);
        var runs = new List<PipelineRunDto>
        {
            Run("failed",    start: base1,             pipeline: "deploy-pipeline", isDeploy: true),
            Run("succeeded", start: base1.AddHours(3), pipeline: "deploy-pipeline", isDeploy: true),
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.MeanTimeToRestoreHours.Should().BeApproximately(3.0, 0.1);
        result.IsMttrEstimated.Should().BeFalse();
    }

    [Fact]
    public async Task ComputeAndSaveAsync_Mttr_FallsBackToAllRuns_WhenNoDeploymentTagged()
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
        result.IsMttrEstimated.Should().BeTrue();
    }

    [Fact]
    public async Task ComputeAndSaveAsync_Mttr_IsZero_WhenNoSuccessAfterFailure()
    {
        SetupRepo([Run("failed", isDeploy: true)]);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.MeanTimeToRestoreHours.Should().Be(0);
    }

    [Fact]
    public async Task ComputeAndSaveAsync_Mttr_IgnoresDifferentPipelineNames()
    {
        var base1 = DateTimeOffset.UtcNow.AddDays(-5);
        var runs = new List<PipelineRunDto>
        {
            Run("failed",    start: base1,             pipeline: "pipe-a", isDeploy: true),
            Run("succeeded", start: base1.AddHours(2), pipeline: "pipe-b", isDeploy: true), // different pipeline
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        // MTTR only matches same pipeline name
        result.MeanTimeToRestoreHours.Should().Be(0);
    }

    [Fact]
    public async Task ComputeAndSaveAsync_Mttr_IgnoresNonDeploymentFailures_WhenDeploymentRunsExist()
    {
        var base1 = DateTimeOffset.UtcNow.AddDays(-5);
        var runs = new List<PipelineRunDto>
        {
            // Non-deployment failure should be ignored when deployment runs exist
            Run("failed",    start: base1,             pipeline: "ci-pipeline"),
            Run("succeeded", start: base1.AddHours(1), pipeline: "ci-pipeline"),
            // Deployment failure takes 5h to restore
            Run("failed",    start: base1,                pipeline: "deploy-pipeline", isDeploy: true),
            Run("succeeded", start: base1.AddHours(5),    pipeline: "deploy-pipeline", isDeploy: true),
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        // Should only measure deployment pipeline MTTR (5h), not CI pipeline (1h)
        result.MeanTimeToRestoreHours.Should().BeApproximately(5.0, 0.1);
        result.IsMttrEstimated.Should().BeFalse();
    }

    // ── Rework Rate — work-item event based ───────────────────────────────────────

    [Fact]
    public async Task ReworkRate_IsZero_AndEstimated_WhenNoWorkItemEvents()
    {
        // No work-item events at all
        SetupRepo([Run("succeeded")], workItemEvents: []);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ReworkRate.Should().Be(0);
        result.IsReworkRateEstimated.Should().BeTrue();
    }

    [Fact]
    public async Task ReworkRate_IsNotEstimated_WhenWorkItemEventsPresent()
    {
        // One completion, no rework
        var events = new List<WorkItemEventDto>
        {
            WiEvent("Active", "Done"),
        };
        SetupRepo([Run("succeeded")], workItemEvents: events);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.IsReworkRateEstimated.Should().BeFalse();
        result.ReworkRate.Should().Be(0);
    }

    [Fact]
    public async Task ReworkRate_ComputesChurnFromWorkItemStateTransitions()
    {
        // 2 completions (Active→Done), 1 rework (Done→Active) → 50 %
        var events = new List<WorkItemEventDto>
        {
            WiEvent("Active", "Done"),
            WiEvent("Active", "Done"),
            WiEvent("Done",   "Active"),
        };
        SetupRepo([Run("succeeded")], workItemEvents: events);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ReworkRate.Should().BeApproximately(50.0, 0.01);
    }

    [Fact]
    public async Task ReworkRate_DoesNotRiseWhenSinglePipelineRunsManyTimes()
    {
        // A single pipeline that ran 100 times must NOT produce a high rework rate.
        // Rework rate is now driven by work-item events, not pipeline run counts.
        var runs = Enumerable.Range(0, 100)
            .Select(_ => Run("succeeded", pipeline: "ci-pipeline"))
            .ToList();
        // No work-item events → rework = 0
        SetupRepo(runs, workItemEvents: []);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ReworkRate.Should().Be(0);
        result.IsReworkRateEstimated.Should().BeTrue();
    }

    [Fact]
    public async Task ReworkRate_IsZero_WhenOnlyCompletions_NoRework()
    {
        var events = new List<WorkItemEventDto>
        {
            WiEvent("Active",      "Done"),
            WiEvent("In Progress", "Resolved"),
            WiEvent("New",         "Closed"),
        };
        SetupRepo([Run("succeeded")], workItemEvents: events);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.ReworkRate.Should().Be(0);
        result.IsReworkRateEstimated.Should().BeFalse();
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
    [InlineData(10, "Elite")]   // 10% ≤ 15% → Elite
    [InlineData(20, "High")]    // 20% ≤ 30% → High
    [InlineData(40, "Medium")]  // 40% ≤ 45% → Medium
    [InlineData(50, "Low")]     // 50% > 45% → Low
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
            Run("failed",    start: t,                pipeline: "deploy-pipeline", isDeploy: true),
            Run("succeeded", start: t.AddHours(hours), pipeline: "deploy-pipeline", isDeploy: true),
        };
        SetupRepo(runs);

        var result = await _sut.ComputeAndSaveAsync("org", "proj", CancellationToken.None);

        result.MttrRating.Should().Be(expected);
    }

    [Theory]
    [InlineData(0,   "Elite")]  // 0 rework transitions → 0 % → Elite
    [InlineData(4,   "Elite")]  // 4/100 completions → 4 % → Elite
    [InlineData(6,   "High")]   // 6 % → High
    [InlineData(15,  "Medium")] // 15 % → Medium
    [InlineData(35,  "Low")]    // 35 % → Low
    public async Task ReworkRateRating_IsCorrect_UsingWorkItemEvents(int reworkCount, string expected)
    {
        int totalCompletions = 100;
        var events = Enumerable.Range(0, totalCompletions)
            .Select(_ => WiEvent("Active", "Done"))
            .Concat(Enumerable.Range(0, reworkCount)
                .Select(_ => WiEvent("Done", "Active")))
            .ToList();
        SetupRepo([Run("succeeded")], workItemEvents: events);

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
        _repoMock.Setup(r => r.GetRunsAsync("myorg", "myproj", 1, 500, It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);
        _repoMock.Setup(r => r.GetWorkItemEventsAsync(
                     "myorg", "myproj", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);
        DoraMetricsDto? saved = null;
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<DoraMetricsDto>(), It.IsAny<CancellationToken>()))
                 .Callback<DoraMetricsDto, CancellationToken>((d, _) => saved = d)
                 .Returns(Task.CompletedTask);

        await _sut.ComputeAndSaveAsync("myorg", "myproj", CancellationToken.None);

        saved!.OrgId.Should().Be("myorg");
        saved.ProjectId.Should().Be("myproj");
    }
}
