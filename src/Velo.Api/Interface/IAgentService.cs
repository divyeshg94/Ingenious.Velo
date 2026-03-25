using Velo.Api.Controllers;

namespace Velo.Api.Interface;

public interface IAgentService
{
    Task<AgentChatResponse> ChatAsync(string projectId, string message, IEnumerable<ChatMessage> history, CancellationToken cancellationToken);
}

// Phase 2: IConfiguration injected here for Foundry endpoint + Managed Identity setup
#pragma warning disable CS9113
public class AgentService(IConfiguration configuration) : IAgentService
#pragma warning restore CS9113
{
    public Task<AgentChatResponse> ChatAsync(string projectId, string message, IEnumerable<ChatMessage> history, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
