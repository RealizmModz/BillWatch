using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillWatch.Services;

public sealed class BillWatchApiClient
{
    private readonly HttpClient _httpClient;

    public BillWatchApiClient(
        HttpClient httpClient)
    {
        _httpClient = httpClient;
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
            await response.Content.ReadFromJsonAsync<LoginResult>(
                cancellationToken: cancellationToken);

        return result
            ?? throw new InvalidOperationException(
                "The login response was empty.");
    }

    public async Task<IReadOnlyList<BillStreamResult>> GetBillStreamsAsync(
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
            await response.Content.ReadFromJsonAsync<List<BillStreamResult>>(
                cancellationToken: cancellationToken);

        return billStreams ?? [];
    }

    public async Task<IReadOnlyList<BankAccountResult>> GetBankAccountsAsync(
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
            await response.Content.ReadFromJsonAsync<List<BankAccountResult>>(
                cancellationToken: cancellationToken);

        return accounts ?? [];
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
                    cancellationToken: cancellationToken);

        return transactions ?? [];
    }

    public async Task<PlaidHostedLinkResult> CreatePlaidLinkSessionAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Post,
                "/api/plaid/link-token",
                accessToken);

        request.Content =
            JsonContent.Create(new { });

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content.ReadFromJsonAsync<PlaidHostedLinkResult>(
                cancellationToken: cancellationToken);

        if (result is null ||
            result.SessionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(result.HostedLinkUrl))
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
                    cancellationToken: cancellationToken);

        return result
            ?? throw new InvalidOperationException(
                "BillWatch did not receive the Plaid Link session status.");
    }

    public async Task<PlaidConnectionResult> ExchangePlaidPublicTokenAsync(
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
            await response.Content.ReadFromJsonAsync<PlaidConnectionResult>(
                cancellationToken: cancellationToken);

        return result
            ?? throw new InvalidOperationException(
                "BillWatch did not receive the saved bank connection.");
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
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