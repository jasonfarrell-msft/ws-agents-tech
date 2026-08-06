namespace ExecutiveDashboard.Providers;

public interface IWorkIqAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(IReadOnlyList<string> scopes, CancellationToken cancellationToken = default);
}
