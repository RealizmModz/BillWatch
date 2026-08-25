namespace BillWatch.Services;

public sealed class BankDataService
{
    private readonly BillWatchApiClient _apiClient;
    private readonly AuthSession _authSession;

    public BankDataService(
        BillWatchApiClient apiClient,
        AuthSession authSession)
    {
        _apiClient = apiClient;
        _authSession = authSession;
    }

    public async Task<IReadOnlyList<BankAccountResult>> GetAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        var accessToken =
            await GetRequiredAccessTokenAsync();

        return await _apiClient.GetBankAccountsAsync(
            accessToken,
            cancellationToken);
    }

    public async Task<IReadOnlyList<BankTransactionResult>> GetTransactionsAsync(
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var accessToken =
            await GetRequiredAccessTokenAsync();

        return await _apiClient.GetBankTransactionsAsync(
            accessToken,
            take,
            cancellationToken);
    }

    private async Task<string> GetRequiredAccessTokenAsync()
    {
        var accessToken =
            await _authSession.GetAccessTokenAsync();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                "You must be signed in to view connected bank data.");
        }

        return accessToken;
    }
}