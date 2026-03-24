using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Velo.Api.Services;
using Velo.Shared.Models;
using Velo.SQL;

namespace Velo.Api.Tests.Services;

public class MetricsRepositoryTests : IDisposable
{
    private readonly VeloDbContext _dbContext;
    private readonly MetricsRepository _sut;

    public MetricsRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<VeloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new VeloDbContext(options);
        _dbContext.CurrentOrgId = "org1";
        _sut = new MetricsRepository(_dbContext, NullLogger<MetricsRepository>.Instance);
    }

    public void Dispose() => _dbContext.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static PipelineRunDto MakeRun(string org = "org1", string proj = "proj1", int pipelineId = 1, string runNum = "1")
        => new()
        {
            Id = Guid.NewGuid(), OrgId = org, ProjectId = proj,
            PipelineName = "CI", RunNumber = runNum, Result = "succeeded",
            StartTime = DateTimeOffset.UtcNow.AddHours(-2),
            FinishTime = DateTimeOffset.UtcNow.AddHours(-1),
            DurationMs = 3_600_000, IsDeployment = false,
            AdoPipelineId = pipelineId, IngestedAt = DateTimeOffset.UtcNow
        };

    private static DoraMetricsDto MakeDoraMetrics(string org = "org1", string proj = "proj1",
        DateTimeOffset? computedAt = null)
        => new()
        {
            Id = Guid.NewGuid(), OrgId = org, ProjectId = proj,
            ComputedAt = computedAt ?? DateTimeOffset.UtcNow,
            PeriodStart = DateTimeOffset.UtcNow.AddDays(-30),
            PeriodEnd = DateTimeOffset.UtcNow,
            DeploymentFrequency = 1.5, DeploymentFrequencyRating = "Elite",
            LeadTimeForChangesHours = 0.5, LeadTimeRating = "Elite",
            ChangeFailureRate = 3.0, ChangeFailureRating = "Elite",
            MeanTimeToRestoreHours = 0.5, MttrRating = "Elite",
            ReworkRate = 2.0, ReworkRateRating = "Elite"
        };

    // ── Pipeline Runs ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveRunAsync_PersistsRun()
    {
        var run = MakeRun();
        await _sut.SaveRunAsync(run, CancellationToken.None);

        var saved = await _dbContext.PipelineRuns.IgnoreQueryFilters().FirstOrDefaultAsync();
        saved.Should().NotBeNull();
        saved!.OrgId.Should().Be("org1");
    }

    [Fact]
    public async Task GetRunsAsync_ReturnsRuns_ForOrgAndProject()
    {
        await _sut.SaveRunAsync(MakeRun(), CancellationToken.None);
        await _sut.SaveRunAsync(MakeRun(proj: "other-proj"), CancellationToken.None);

        var runs = (await _sut.GetRunsAsync("org1", "proj1", 1, 50, CancellationToken.None)).ToList();

        runs.Should().ContainSingle();
        runs[0].ProjectId.Should().Be("proj1");
    }

    [Fact]
    public async Task GetRunsAsync_ReturnsEmpty_WhenNoRuns()
    {
        var runs = await _sut.GetRunsAsync("org1", "no-project", 1, 50, CancellationToken.None);
        runs.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRunsAsync_RespectsPagination()
    {
        for (int i = 0; i < 10; i++)
            await _sut.SaveRunAsync(MakeRun(runNum: i.ToString()), CancellationToken.None);

        var page1 = (await _sut.GetRunsAsync("org1", "proj1", 1, 3, CancellationToken.None)).ToList();

        page1.Should().HaveCount(3);
    }

    [Fact]
    public async Task RunExistsAsync_ReturnsTrue_WhenRunExists()
    {
        await _sut.SaveRunAsync(MakeRun(pipelineId: 99, runNum: "42"), CancellationToken.None);

        var exists = await _sut.RunExistsAsync("org1", "proj1", 99, "42", CancellationToken.None);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task RunExistsAsync_ReturnsFalse_WhenRunDoesNotExist()
    {
        var exists = await _sut.RunExistsAsync("org1", "proj1", 99, "no-run", CancellationToken.None);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task RunExistsAsync_ReturnsFalse_WhenWrongOrg()
    {
        await _sut.SaveRunAsync(MakeRun(org: "other-org", pipelineId: 1, runNum: "1"), CancellationToken.None);

        var exists = await _sut.RunExistsAsync("org1", "proj1", 1, "1", CancellationToken.None);
        exists.Should().BeFalse();
    }

    // ── DORA Metrics ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_AndGetLatestAsync_RoundTrip()
    {
        var dto = MakeDoraMetrics();
        await _sut.SaveAsync(dto, CancellationToken.None);

        var latest = await _sut.GetLatestAsync("org1", "proj1", CancellationToken.None);

        latest.Should().NotBeNull();
        latest!.DeploymentFrequency.Should().Be(1.5);
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsNull_WhenNoMetrics()
    {
        var latest = await _sut.GetLatestAsync("org1", "no-project", CancellationToken.None);
        latest.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsMostRecent_WhenMultipleExist()
    {
        await _sut.SaveAsync(MakeDoraMetrics(computedAt: DateTimeOffset.UtcNow.AddDays(-5)), CancellationToken.None);
        await _sut.SaveAsync(MakeDoraMetrics(computedAt: DateTimeOffset.UtcNow), CancellationToken.None);

        var latest = await _sut.GetLatestAsync("org1", "proj1", CancellationToken.None);

        latest.Should().NotBeNull();
        latest!.ComputedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsOnlyMetricsWithinDateRange()
    {
        var now = DateTimeOffset.UtcNow;
        await _sut.SaveAsync(MakeDoraMetrics(computedAt: now.AddDays(-5)), CancellationToken.None);   // in range
        await _sut.SaveAsync(MakeDoraMetrics(computedAt: now.AddDays(-40)), CancellationToken.None);  // out of range

        var history = (await _sut.GetHistoryAsync("org1", "proj1", now.AddDays(-10), now, CancellationToken.None)).ToList();

        history.Should().ContainSingle();
        history[0].ComputedAt.Should().BeCloseTo(now.AddDays(-5), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsEmpty_WhenNoMetricsInRange()
    {
        await _sut.SaveAsync(MakeDoraMetrics(computedAt: DateTimeOffset.UtcNow.AddDays(-60)), CancellationToken.None);

        var history = await _sut.GetHistoryAsync("org1", "proj1",
            DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow, CancellationToken.None);

        history.Should().BeEmpty();
    }

    // ── Team Health ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveTeamHealthAsync_AndGetTeamHealthAsync_RoundTrip()
    {
        var dto = new TeamHealthDto
        {
            Id = Guid.NewGuid(), OrgId = "org1", ProjectId = "proj1",
            ComputedAt = DateTimeOffset.UtcNow,
            TestPassRate = 95, PrApprovalRate = 80, FlakyTestRate = 5,
            CodingTimeHours = 4, ReviewTimeHours = 2, MergeTimeHours = 1,
            DeployTimeHours = 0.5, DeploymentRiskScore = 10
        };

        await _sut.SaveTeamHealthAsync(dto, CancellationToken.None);
        var saved = await _sut.GetTeamHealthAsync("org1", "proj1", CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.TestPassRate.Should().Be(95);
        saved.PrApprovalRate.Should().Be(80);
    }

    [Fact]
    public async Task GetTeamHealthAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _sut.GetTeamHealthAsync("org1", "no-project", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTeamHealthAsync_ReturnsMostRecent_WhenMultipleExist()
    {
        var old = new TeamHealthDto
        {
            Id = Guid.NewGuid(), OrgId = "org1", ProjectId = "proj1",
            ComputedAt = DateTimeOffset.UtcNow.AddDays(-10), TestPassRate = 50
        };
        var recent = new TeamHealthDto
        {
            Id = Guid.NewGuid(), OrgId = "org1", ProjectId = "proj1",
            ComputedAt = DateTimeOffset.UtcNow, TestPassRate = 90
        };
        await _sut.SaveTeamHealthAsync(old, CancellationToken.None);
        await _sut.SaveTeamHealthAsync(recent, CancellationToken.None);

        var saved = await _sut.GetTeamHealthAsync("org1", "proj1", CancellationToken.None);

        saved!.TestPassRate.Should().Be(90);
    }

    // ── PR Events ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SavePrEventAsync_AndGetPrEventsAsync_RoundTrip()
    {
        var pr = new PullRequestEventDto
        {
            Id = Guid.NewGuid(), OrgId = "org1", ProjectId = "proj1",
            PrId = 42, Title = "My PR", Status = "completed",
            SourceBranch = "feature", TargetBranch = "main",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
            ClosedAt = DateTimeOffset.UtcNow.AddDays(-2),
            IsApproved = true, ReviewerCount = 2,
            IngestedAt = DateTimeOffset.UtcNow
        };

        await _sut.SavePrEventAsync(pr, CancellationToken.None);
        var events = (await _sut.GetPrEventsAsync("org1", "proj1",
            DateTimeOffset.UtcNow.AddDays(-10), CancellationToken.None)).ToList();

        events.Should().ContainSingle();
        events[0].PrId.Should().Be(42);
        events[0].IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task GetPrEventsAsync_ReturnsEmpty_WhenNoEvents()
    {
        var events = await _sut.GetPrEventsAsync("org1", "proj1",
            DateTimeOffset.UtcNow.AddDays(-10), CancellationToken.None);
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPrEventsAsync_ReturnsOnlyEventsAfterFrom()
    {
        var old = new PullRequestEventDto
        {
            Id = Guid.NewGuid(), OrgId = "org1", ProjectId = "proj1",
            PrId = 1, Status = "completed",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-40),
            IngestedAt = DateTimeOffset.UtcNow
        };
        var recent = new PullRequestEventDto
        {
            Id = Guid.NewGuid(), OrgId = "org1", ProjectId = "proj1",
            PrId = 2, Status = "completed",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
            IngestedAt = DateTimeOffset.UtcNow
        };
        await _sut.SavePrEventAsync(old, CancellationToken.None);
        await _sut.SavePrEventAsync(recent, CancellationToken.None);

        var events = (await _sut.GetPrEventsAsync("org1", "proj1",
            DateTimeOffset.UtcNow.AddDays(-10), CancellationToken.None)).ToList();

        events.Should().ContainSingle();
        events[0].PrId.Should().Be(2);
    }

    [Fact]
    public async Task PrEventExistsAsync_ReturnsTrue_WhenExists()
    {
        var pr = new PullRequestEventDto
        {
            Id = Guid.NewGuid(), OrgId = "org1", ProjectId = "proj1",
            PrId = 55, Status = "active",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            IngestedAt = DateTimeOffset.UtcNow
        };
        await _sut.SavePrEventAsync(pr, CancellationToken.None);

        var exists = await _sut.PrEventExistsAsync("org1", "proj1", 55, "active", CancellationToken.None);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task PrEventExistsAsync_ReturnsFalse_WhenNotExists()
    {
        var exists = await _sut.PrEventExistsAsync("org1", "proj1", 999, "active", CancellationToken.None);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task PrEventExistsAsync_ReturnsFalse_WhenStatusDiffers()
    {
        var pr = new PullRequestEventDto
        {
            Id = Guid.NewGuid(), OrgId = "org1", ProjectId = "proj1",
            PrId = 77, Status = "active",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            IngestedAt = DateTimeOffset.UtcNow
        };
        await _sut.SavePrEventAsync(pr, CancellationToken.None);

        var exists = await _sut.PrEventExistsAsync("org1", "proj1", 77, "completed", CancellationToken.None);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task SavePrEventAsync_Upserts_OnSameOrgPrIdStatus()
    {
        var pr = new PullRequestEventDto
        {
            Id = Guid.NewGuid(), OrgId = "org1", ProjectId = "proj1",
            PrId = 99, Status = "active", IsApproved = false,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            IngestedAt = DateTimeOffset.UtcNow
        };
        await _sut.SavePrEventAsync(pr, CancellationToken.None);
        pr.IsApproved = true;
        await _sut.SavePrEventAsync(pr, CancellationToken.None);

        var events = (await _sut.GetPrEventsAsync("org1", "proj1",
            DateTimeOffset.UtcNow.AddDays(-5), CancellationToken.None)).ToList();

        events.Should().ContainSingle();
        events[0].IsApproved.Should().BeTrue();
    }

    // ── Org Context ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveOrgContextAsync_AndGetOrgContextAsync_RoundTrip()
    {
        var org = new OrgContextDto
        {
            OrgId = "org1", OrgUrl = "https://dev.azure.com/org1",
            DisplayName = "Org One", IsPremium = false,
            DailyTokenBudget = 50_000, RegisteredAt = DateTimeOffset.UtcNow
        };

        await _sut.SaveOrgContextAsync(org, CancellationToken.None);
        var saved = await _sut.GetOrgContextAsync("org1", CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.DisplayName.Should().Be("Org One");
        saved.DailyTokenBudget.Should().Be(50_000);
    }

    [Fact]
    public async Task GetOrgContextAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _sut.GetOrgContextAsync("nonexistent-org", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveOrgContextAsync_UpdatesExistingOrg()
    {
        var org = new OrgContextDto
        {
            OrgId = "org1", OrgUrl = "https://dev.azure.com/org1",
            DisplayName = "Original", RegisteredAt = DateTimeOffset.UtcNow
        };
        await _sut.SaveOrgContextAsync(org, CancellationToken.None);

        org.DisplayName = "Updated";
        await _sut.SaveOrgContextAsync(org, CancellationToken.None);

        var saved = await _sut.GetOrgContextAsync("org1", CancellationToken.None);
        saved!.DisplayName.Should().Be("Updated");
    }
}
