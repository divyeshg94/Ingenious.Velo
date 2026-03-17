using Velo.SQL;

namespace Velo.Api.Services;

public interface IConnectionService
{
    Task RegisterAsync(string orgUrl, string personalAccessToken, CancellationToken cancellationToken);
    Task RemoveAsync(CancellationToken cancellationToken);
}

public class ConnectionService(VeloDbContext db) : IConnectionService
{
    public Task RegisterAsync(string orgUrl, string personalAccessToken, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task RemoveAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
