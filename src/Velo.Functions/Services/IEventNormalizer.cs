using Velo.Functions.Models;

namespace Velo.Functions.Services;

public interface IEventNormalizer
{
    Task NormalizeAndPersistAsync(ServiceHookPayload payload);
}

public class EventNormalizer : IEventNormalizer
{
    private const string BuildCompleted = "build.complete";
    private const string PrMerged = "git.pullrequest.merged";
    private const string WorkItemUpdated = "workitem.updated";

    public Task NormalizeAndPersistAsync(ServiceHookPayload payload)
    {
        return payload.EventType switch
        {
            BuildCompleted => HandleBuildCompletedAsync(payload),
            PrMerged => HandlePrMergedAsync(payload),
            WorkItemUpdated => HandleWorkItemUpdatedAsync(payload),
            _ => Task.CompletedTask,
        };
    }

    private Task HandleBuildCompletedAsync(ServiceHookPayload payload)
    {
        // TODO: extract PipelineRunEvent from payload.Resource and persist to SQL
        throw new NotImplementedException();
    }

    private Task HandlePrMergedAsync(ServiceHookPayload payload)
    {
        // TODO: extract PR data and persist for lead time computation
        throw new NotImplementedException();
    }

    private Task HandleWorkItemUpdatedAsync(ServiceHookPayload payload)
    {
        // TODO: track work item state transitions for rework rate
        throw new NotImplementedException();
    }
}
