using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Velo.Api.Services;
using Velo.SQL;

namespace Velo.Api.Tests.Services;

public class FeedbackServiceTests : IDisposable
{
    private readonly VeloDbContext _dbContext;

    public FeedbackServiceTests()
    {
        var options = new DbContextOptionsBuilder<VeloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new VeloDbContext(options)
        {
            CurrentOrgId = "test-org"
        };
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task SubmitFeedbackAsync_QueuesNotification_WhenOwnerEmailConfigured()
    {
        var queueMock = new Mock<IFeedbackNotificationQueue>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Smtp:OwnerEmail"] = "owner@example.com" })
            .Build();
        var sut = new FeedbackService(
            _dbContext,
            configuration,
            queueMock.Object,
            NullLogger<FeedbackService>.Instance);

        var feedbackId = await sut.SubmitFeedbackAsync(
            "Bug",
            "Something broke",
            "project-1",
            "user@example.com",
            CancellationToken.None);

        feedbackId.Should().NotBeEmpty();
        queueMock.Verify(q => q.Enqueue(It.Is<FeedbackNotificationWorkItem>(item =>
            item.OwnerEmail == "owner@example.com" &&
            item.FeedbackId == feedbackId &&
            item.OrgId == "test-org" &&
            item.ProjectId == "project-1")), Times.Once);
    }

    [Fact]
    public async Task SubmitFeedbackAsync_DoesNotQueueNotification_WhenOwnerEmailMissing()
    {
        var queueMock = new Mock<IFeedbackNotificationQueue>();
        var configuration = new ConfigurationBuilder().Build();
        var sut = new FeedbackService(
            _dbContext,
            configuration,
            queueMock.Object,
            NullLogger<FeedbackService>.Instance);

        var feedbackId = await sut.SubmitFeedbackAsync(
            "FeatureRequest",
            "Please add this",
            null,
            "user@example.com",
            CancellationToken.None);

        feedbackId.Should().NotBeEmpty();
        queueMock.Verify(q => q.Enqueue(It.IsAny<FeedbackNotificationWorkItem>()), Times.Never);
    }
}
