using System.Threading.Channels;
using Velo.Api.Logging;

namespace Velo.Api.Services;

public record FeedbackNotificationWorkItem(
    string OwnerEmail,
    Guid FeedbackId,
    string FeedbackType,
    string Message,
    string OrgId,
    string? ProjectId,
    string? UserId);

public interface IFeedbackNotificationQueue
{
    void Enqueue(FeedbackNotificationWorkItem workItem);
    ValueTask<FeedbackNotificationWorkItem> DequeueAsync(CancellationToken cancellationToken);
}

public class FeedbackNotificationQueue : IFeedbackNotificationQueue
{
    private readonly Channel<FeedbackNotificationWorkItem> _queue = Channel.CreateUnbounded<FeedbackNotificationWorkItem>();

    public void Enqueue(FeedbackNotificationWorkItem workItem)
    {
        if (!_queue.Writer.TryWrite(workItem))
            throw new InvalidOperationException("Unable to queue feedback notification.");
    }

    public ValueTask<FeedbackNotificationWorkItem> DequeueAsync(CancellationToken cancellationToken)
        => _queue.Reader.ReadAsync(cancellationToken);
}

public class FeedbackNotificationWorker(
    IServiceScopeFactory scopeFactory,
    IFeedbackNotificationQueue notificationQueue,
    ILogger<FeedbackNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            FeedbackNotificationWorkItem workItem;
            try
            {
                workItem = await notificationQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

                await emailService.SendFeedbackNotificationAsync(
                    workItem.OwnerEmail,
                    workItem.FeedbackType,
                    workItem.Message,
                    workItem.OrgId,
                    workItem.ProjectId,
                    workItem.UserId,
                    timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug(
                    "Email notification timed out for FeedbackId={FeedbackId}",
                    workItem.FeedbackId);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to send queued feedback notification for FeedbackId={FeedbackId} OrgId={OrgId}",
                    workItem.FeedbackId,
                    LogSanitizer.SanitiseForLog(workItem.OrgId));
            }
        }
    }
}
