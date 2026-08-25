namespace BillWatch.Services;

public sealed class BillStreamService
{
    private readonly BillWatchApiClient _apiClient;
    private readonly AuthSession _authSession;

    public BillStreamService(
        BillWatchApiClient apiClient,
        AuthSession authSession)
    {
        _apiClient = apiClient;
        _authSession = authSession;
    }

    public async Task<IReadOnlyList<BillStreamResult>> GetBillStreamsAsync(
        CancellationToken cancellationToken = default)
    {
        var accessToken =
            await _authSession.GetAccessTokenAsync();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return [];
        }

        return await _apiClient.GetBillStreamsAsync(
            accessToken,
            cancellationToken);
    }
}