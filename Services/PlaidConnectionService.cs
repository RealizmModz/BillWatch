namespace BillWatch.Services;

public sealed class PlaidConnectionService
{
    private readonly BillWatchApiClient _apiClient;
    private readonly AuthSession _authSession;

    public PlaidConnectionService(
        BillWatchApiClient apiClient,
        AuthSession authSession)
    {
        _apiClient = apiClient;
        _authSession = authSession;
    }

    public async Task<PlaidHostedLinkResult> CreateLinkSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var accessToken =
            await GetRequiredAccessTokenAsync();

        return await _apiClient.CreatePlaidLinkSessionAsync(
            accessToken,
            cancellationToken);
    }

    public async Task<PlaidHostedLinkCompletionResult> CompleteLinkSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Plaid Link session ID is required.",
                nameof(sessionId));
        }

        var accessToken =
            await GetRequiredAccessTokenAsync();

        return await _apiClient.CompletePlaidLinkSessionAsync(
            accessToken,
            sessionId,
            cancellationToken);
    }

    public async Task<PlaidConnectionResult> ExchangePublicTokenAsync(
        string publicToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicToken))
        {
            throw new ArgumentException(
                "Plaid public token is required.",
                nameof(publicToken));
        }

        var accessToken =
            await GetRequiredAccessTokenAsync();

        return await _apiClient.ExchangePlaidPublicTokenAsync(
            accessToken,
            publicToken,
            cancellationToken);
    }

    private async Task<string> GetRequiredAccessTokenAsync()
    {
        var accessToken =
            await _authSession.GetAccessTokenAsync();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                "You must be signed in before connecting a bank.");
        }

        return accessToken;
    }
}