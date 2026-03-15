using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Velo.Functions.Services;

namespace Velo.Functions.Triggers;

/// <summary>
/// Timer-triggered function that computes all five DORA metrics on a configurable schedule.
/// Default: every hour. No AI cost — pure SQL aggregation.
/// </summary>
public class MetricsComputeTimer(IMetricsEngine metricsEngine, ILogger<MetricsComputeTimer> logger)
{
    // Runs every hour by default. Adjust via METRICS_COMPUTE_SCHEDULE app setting.
    [Function(nameof(MetricsComputeTimer))]
    public async Task Run([TimerTrigger("%METRICS_COMPUTE_SCHEDULE%")] TimerInfo timer, FunctionContext context)
    {
        logger.LogInformation("DORA metrics computation started at {Time}", DateTimeOffset.UtcNow);

        if (timer.ScheduleStatus?.Last is not null)
        {
            logger.LogInformation("Previous run: {Last}", timer.ScheduleStatus.Last);
        }

        await metricsEngine.ComputeAllOrgsAsync(CancellationToken.None);

        logger.LogInformation("DORA metrics computation completed at {Time}", DateTimeOffset.UtcNow);
    }
}
