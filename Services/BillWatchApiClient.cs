using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillWatch.Services;

public sealed class BillWatchApiClient
{
    private readonly HttpClient _httpClient;

    public BillWatchApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<LoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                email,
                password
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<LoginResult>(
                cancellationToken: cancellationToken);

        return result
            ?? throw new InvalidOperationException(
                "The login response was empty.");
    }

    public async Task<IReadOnlyList<BillStreamResult>> GetBillStreamsAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/bill-streams");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var billStreams = await response.Content
            .ReadFromJsonAsync<List<BillStreamResult>>(
                cancellationToken: cancellationToken);

        return billStreams ?? [];
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