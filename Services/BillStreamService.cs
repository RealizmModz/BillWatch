using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillWatch.Services;

public sealed class BillStreamService
{
    private readonly BillWatchApiClient
        _apiClient;

    private readonly AuthenticationService
        _authenticationService;

    private readonly HttpClient
        _httpClient;

    public BillStreamService(
        BillWatchApiClient apiClient,
        AuthenticationService authenticationService,
        HttpClient httpClient)
    {
        _apiClient =
            apiClient;

        _authenticationService =
            authenticationService;

        _httpClient =
            httpClient;
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

    public async Task<BillStatementUploadStatusResult>
        GetStatementUploadStatusAsync(
            Guid billStreamId,
            Guid uploadId,
            CancellationToken cancellationToken = default)
    {
        if (billStreamId == Guid.Empty)
        {
            throw new ArgumentException(
                "Bill stream ID is required.",
                nameof(billStreamId));
        }

        if (uploadId == Guid.Empty)
        {
            throw new ArgumentException(
                "Statement upload ID is required.",
                nameof(uploadId));
        }

        var accessToken =
            await _authenticationService
                .GetValidAccessTokenAsync(
                    cancellationToken);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/bill-streams/{billStreamId}/statement-uploads/{uploadId}");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        using var response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content
                .ReadFromJsonAsync<BillStatementUploadStatusResult>(
                    cancellationToken:
                        cancellationToken);

        return result
            ?? throw new InvalidOperationException(
                "The statement processing status response was empty.");
    }
}

public sealed record BillStatementUploadStatusResult(
    Guid Id,
    Guid BillStreamId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);