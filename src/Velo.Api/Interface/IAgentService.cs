using Velo.Api.Controllers;

namespace Velo.Api.Interface;

public interface IAgentService
{
    Task<AgentChatResponse> ChatAsync(string projectId, string message, IEnumerable<ChatMessage> history, CancellationToken cancellationToken);
}

public class AgentService(IConfiguration configuration) : IAgentService
{
    public Task<AgentChatResponse> ChatAsync(string projectId, string message, IEnumerable<ChatMessage> history, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
