using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillWatch.Services;

public sealed class BillAlertService
{
    private readonly HttpClient
        _httpClient;

    private readonly AuthenticationService
        _authenticationService;

    public BillAlertService(
        HttpClient httpClient,
        AuthenticationService authenticationService)
    {
        _httpClient =
            httpClient;

        _authenticationService =
            authenticationService;
    }

    public async Task<IReadOnlyList<BillAlertResult>>
        GetAlertsAsync(
            bool includeDismissed = false,
            bool unreadOnly = false,
            int take = 50,
            CancellationToken cancellationToken = default)
    {
        take =
            Math.Clamp(
                take,
                1,
                100);

        var accessToken =
            await _authenticationService
                .GetValidAccessTokenAsync(
                    cancellationToken);

        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Get,
                $"/api/alerts?includeDismissed={includeDismissed.ToString().ToLowerInvariant()}&unreadOnly={unreadOnly.ToString().ToLowerInvariant()}&take={take}",
                accessToken);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var alerts =
            await response.Content
                .ReadFromJsonAsync<List<BillAlertResult>>(
                    cancellationToken:
                        cancellationToken);

        return alerts ?? [];
    }

    public async Task MarkReadAsync(
        Guid alertId,
        CancellationToken cancellationToken = default)
    {
        if (alertId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Alert ID is required.",
                nameof(alertId));
        }

        await SendMutationAsync(
            $"/api/alerts/{alertId}/read",
            cancellationToken);
    }

    public async Task DismissAsync(
        Guid alertId,
        CancellationToken cancellationToken = default)
    {
        if (alertId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Alert ID is required.",
                nameof(alertId));
        }

        await SendMutationAsync(
            $"/api/alerts/{alertId}/dismiss",
            cancellationToken);
    }

    private async Task SendMutationAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        var accessToken =
            await _authenticationService
                .GetValidAccessTokenAsync(
                    cancellationToken);

        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Post,
                requestUri,
                accessToken);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();
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
}

public sealed record BillAlertResult(
    Guid Id,
    Guid? BillStreamId,
    Guid? BillChangeId,
    string AlertType,
    string Severity,
    string Title,
    string Message,
    bool IsRead,
    bool IsDismissed,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);