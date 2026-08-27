namespace BillWatch.Services;

public sealed class BillStreamService
{
    private readonly BillWatchApiClient
        _apiClient;

    private readonly AuthenticationService
        _authenticationService;

    public BillStreamService(
        BillWatchApiClient apiClient,
        AuthenticationService authenticationService)
    {
        _apiClient =
            apiClient;

        _authenticationService =
            authenticationService;
    }

    public async Task<IReadOnlyList<BillStreamResult>>
        GetBillStreamsAsync(
            CancellationToken cancellationToken = default)
    {
        var accessToken =
            await _authenticationService
                .GetValidAccessTokenAsync(
                    cancellationToken);

        return await _apiClient
            .GetBillStreamsAsync(
                accessToken,
                cancellationToken);
    }
}