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

    public async Task<BillStreamDetailResult>
        GetBillStreamDetailAsync(
            Guid billStreamId,
            CancellationToken cancellationToken = default)
    {
        if (billStreamId == Guid.Empty)
        {
            throw new ArgumentException(
                "Bill stream ID is required.",
                nameof(billStreamId));
        }

        var accessToken =
            await _authenticationService
                .GetValidAccessTokenAsync(
                    cancellationToken);

        return await _apiClient
            .GetBillStreamDetailAsync(
                accessToken,
                billStreamId,
                cancellationToken);
    }

    public async Task<BillStatementUploadResult>
        UploadStatementAsync(
            Guid billStreamId,
            Stream fileStream,
            string fileName,
            string? mediaType = null,
            CancellationToken cancellationToken = default)
    {
        if (billStreamId == Guid.Empty)
        {
            throw new ArgumentException(
                "Bill stream ID is required.",
                nameof(billStreamId));
        }

        var accessToken =
            await _authenticationService
                .GetValidAccessTokenAsync(
                    cancellationToken);

        return await _apiClient
            .UploadBillStatementAsync(
                accessToken,
                billStreamId,
                fileStream,
                fileName,
                mediaType,
                cancellationToken);
    }
}