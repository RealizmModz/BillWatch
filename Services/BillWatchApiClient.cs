using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillWatch.Services;

public sealed class BillWatchApiClient
{
    private readonly HttpClient _httpClient;

    public BillWatchApiClient(
        HttpClient httpClient)
    {
        _httpClient =
            httpClient;
    }

    public async Task<LoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _httpClient.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email,
                    password
                },
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content
                .ReadFromJsonAsync<LoginResult>(
                    cancellationToken:
                        cancellationToken);

        return result
            ?? throw new InvalidOperationException(
                "The login response was empty.");
    }

    public async Task<LoginResult>
        RefreshAccessTokenAsync(
            string refreshToken,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                refreshToken))
        {
            throw new ArgumentException(
                "Refresh token is required.",
                nameof(refreshToken));
        }

        using var response =
            await _httpClient.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken
                },
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content
                .ReadFromJsonAsync<LoginResult>(
                    cancellationToken:
                        cancellationToken);

        return result
            ?? throw new InvalidOperationException(
                "The token refresh response was empty.");
    }

    public async Task<IReadOnlyList<BillStreamResult>>
        GetBillStreamsAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
    {
        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Get,
                "/api/bill-streams",
                accessToken);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var billStreams =
            await response.Content
                .ReadFromJsonAsync<List<BillStreamResult>>(
                    cancellationToken:
                        cancellationToken);

        return billStreams ?? [];
    }

    public async Task<BillStreamDetailResult>
        GetBillStreamDetailAsync(
            string accessToken,
            Guid billStreamId,
            CancellationToken cancellationToken = default)
    {
        if (billStreamId == Guid.Empty)
        {
            throw new ArgumentException(
                "Bill stream ID is required.",
                nameof(billStreamId));
        }

        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Get,
                $"/api/bill-streams/{billStreamId}",
                accessToken);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var detail =
            await response.Content
                .ReadFromJsonAsync<BillStreamDetailResult>(
                    cancellationToken:
                        cancellationToken);

        return detail
            ?? throw new InvalidOperationException(
                "The bill detail response was empty.");
    }

    public async Task<BillStatementUploadResult>
        UploadBillStatementAsync(
            string accessToken,
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

        ArgumentNullException.ThrowIfNull(
            fileStream);

        if (!fileStream.CanRead)
        {
            throw new ArgumentException(
                "The selected file cannot be read.",
                nameof(fileStream));
        }

        if (string.IsNullOrWhiteSpace(
                fileName))
        {
            throw new ArgumentException(
                "File name is required.",
                nameof(fileName));
        }

        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Post,
                $"/api/bill-streams/{billStreamId}/statement-uploads",
                accessToken);

        using var multipart =
            new MultipartFormDataContent();

        using var fileContent =
            new StreamContent(
                fileStream);

        if (!string.IsNullOrWhiteSpace(
                mediaType))
        {
            fileContent.Headers.ContentType =
                new MediaTypeHeaderValue(
                    mediaType);
        }

        multipart.Add(
            fileContent,
            "file",
            fileName);

        request.Content =
            multipart;

        using var response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error =
                await response.Content
                    .ReadFromJsonAsync<ApiErrorResult>(
                        cancellationToken:
                            cancellationToken);

            throw new HttpRequestException(
                error?.Message
                ?? "BillWatch could not upload this statement.",
                null,
                response.StatusCode);
        }

        var result =
            await response.Content
                .ReadFromJsonAsync<BillStatementUploadResult>(
                    cancellationToken:
                        cancellationToken);

        return result
            ?? throw new InvalidOperationException(
                "The statement upload response was empty.");
    }

    public async Task<IReadOnlyList<BankAccountResult>>
        GetBankAccountsAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
    {
        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Get,
                "/api/bank-accounts",
                accessToken);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var accounts =
            await response.Content
                .ReadFromJsonAsync<List<BankAccountResult>>(
                    cancellationToken:
                        cancellationToken);

        return accounts ?? [];
    }

    public async Task<IReadOnlyList<BankConnectionResult>>
        GetBankConnectionsAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
    {
        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Get,
                "/api/bank-connections",
                accessToken);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var connections =
            await response.Content
                .ReadFromJsonAsync<List<BankConnectionResult>>(
                    cancellationToken:
                        cancellationToken);

        return connections ?? [];
    }

    public async Task DisconnectBankConnectionAsync(
        string accessToken,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        if (connectionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Bank connection ID is required.",
                nameof(connectionId));
        }

        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Delete,
                $"/api/bank-connections/{connectionId}",
                accessToken);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<BankTransactionResult>>
        GetBankTransactionsAsync(
            string accessToken,
            int take = 100,
            CancellationToken cancellationToken = default)
    {
        take =
            Math.Clamp(
                take,
                1,
                500);

        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Get,
                $"/api/bank-transactions?take={take}",
                accessToken);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var transactions =
            await response.Content
                .ReadFromJsonAsync<List<BankTransactionResult>>(
                    cancellationToken:
                        cancellationToken);

        return transactions ?? [];
    }

    public async Task<PlaidHostedLinkResult>
        CreatePlaidLinkSessionAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
    {
        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Post,
                "/api/plaid/link-token",
                accessToken);

        request.Content =
            JsonContent.Create(
                new { });

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content
                .ReadFromJsonAsync<PlaidHostedLinkResult>(
                    cancellationToken:
                        cancellationToken);

        if (result is null ||
            result.SessionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(
                result.HostedLinkUrl))
        {
            throw new InvalidOperationException(
                "BillWatch did not receive a valid Plaid Link session.");
        }

        return result;
    }

    public async Task<PlaidHostedLinkCompletionResult>
        CompletePlaidLinkSessionAsync(
            string accessToken,
            Guid sessionId,
            CancellationToken cancellationToken = default)
    {
        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Post,
                $"/api/plaid/link-session/{sessionId}/complete",
                accessToken);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content
                .ReadFromJsonAsync<PlaidHostedLinkCompletionResult>(
                    cancellationToken:
                        cancellationToken);

        return result
            ?? throw new InvalidOperationException(
                "BillWatch did not receive the Plaid Link session status.");
    }

    public async Task<PlaidConnectionResult>
        ExchangePlaidPublicTokenAsync(
            string accessToken,
            string publicToken,
            CancellationToken cancellationToken = default)
    {
        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Post,
                "/api/plaid/exchange-public-token",
                accessToken);

        request.Content =
            JsonContent.Create(
                new
                {
                    publicToken
                });

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content
                .ReadFromJsonAsync<PlaidConnectionResult>(
                    cancellationToken:
                        cancellationToken);

        return result
            ?? throw new InvalidOperationException(
                "BillWatch did not receive the saved bank connection.");
    }

    private static HttpRequestMessage
        CreateAuthorizedRequest(
            HttpMethod method,
            string requestUri,
            string accessToken)
    {
        var request =
            new HttpRequestMessage(
                method,
                requestUri);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        return request;
    }

    private sealed record ApiErrorResult(
        string Message);
}

public sealed record LoginResult(
    string TokenType,
    string AccessToken,
    long ExpiresIn,
    string RefreshToken);

public sealed record BillStreamResult(
    Guid Id,
    string ProviderName,
    string Category,
    bool IsActive,
    decimal CurrentAmount,
    decimal PreviousAverage);

public sealed record BillStreamDetailResult(
    Guid Id,
    string ProviderName,
    string Category,
    bool IsActive,
    decimal CurrentAmount,
    decimal PreviousAverage,
    IReadOnlyList<BillStatementHistoryResult> Statements,
    IReadOnlyList<BillChangeResult> Changes);

public sealed record BillStatementHistoryResult(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly? StatementDate,
    DateOnly? DueDate,
    decimal TotalAmount,
    string CurrencyCode);

public sealed record BillChangeResult(
    Guid Id,
    Guid? PreviousStatementId,
    Guid CurrentStatementId,
    string ChangeType,
    string Confidence,
    string Description,
    decimal PreviousAmount,
    decimal CurrentAmount,
    decimal AmountDifference,
    decimal AnnualizedImpact,
    bool IsAcknowledged,
    DateTimeOffset DetectedAtUtc);

public sealed record BillStatementUploadResult(
    Guid Id,
    Guid BillStreamId,
    string MediaType,
    string FileExtension,
    long SizeBytes,
    string Status,
    DateTimeOffset CreatedAtUtc);

public sealed record BankAccountResult(
    Guid Id,
    Guid BankConnectionId,
    string InstitutionName,
    string Name,
    string? OfficialName,
    string? Mask,
    string AccountType,
    string? AccountSubtype,
    bool IsActive);

public enum BankConnectionStatus
{
    Active = 0,
    RequiresAttention = 1,
    Disconnected = 2
}

public sealed record BankConnectionResult(
    Guid Id,
    string InstitutionName,
    BankConnectionStatus Status,
    DateTimeOffset? LastSuccessfulSyncAtUtc,
    DateTimeOffset CreatedAtUtc);

public sealed record BankTransactionResult(
    Guid Id,
    Guid BankAccountId,
    string InstitutionName,
    string AccountName,
    string? AccountMask,
    string Name,
    string? MerchantName,
    decimal Amount,
    string? IsoCurrencyCode,
    DateOnly PostedDate,
    DateOnly? AuthorizedDate,
    bool IsPending,
    string? CategoryPrimary,
    string? CategoryDetailed);

public sealed record PlaidHostedLinkResult(
    Guid SessionId,
    string HostedLinkUrl);

public sealed record PlaidHostedLinkCompletionResult(
    Guid SessionId,
    string Status,
    PlaidConnectionResult? Connection);

public sealed record PlaidConnectionResult(
    Guid Id,
    string InstitutionName,
    string Status);