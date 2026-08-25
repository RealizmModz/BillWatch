namespace BillWatch.Services;

public sealed class AuthenticationService
{
    private readonly BillWatchApiClient _apiClient;
    private readonly AuthSession _authSession;

    public AuthenticationService(
        BillWatchApiClient apiClient,
        AuthSession authSession)
    {
        _apiClient = apiClient;
        _authSession = authSession;
    }

    public async Task LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var result = await _apiClient.LoginAsync(
            email,
            password,
            cancellationToken);

        await _authSession.SaveAsync(
            result.AccessToken,
            result.RefreshToken);
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        var accessToken =
            await _authSession.GetAccessTokenAsync();

        return !string.IsNullOrWhiteSpace(accessToken);
    }

    public void Logout()
    {
        _authSession.Clear();
    }
}