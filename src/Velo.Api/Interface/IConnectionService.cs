namespace Velo.Api.Interface;

public interface IConnectionService
{
    Task RegisterAsync(string orgUrl, string personalAccessToken, CancellationToken cancellationToken);
    Task RemoveAsync(CancellationToken cancellationToken);
}
