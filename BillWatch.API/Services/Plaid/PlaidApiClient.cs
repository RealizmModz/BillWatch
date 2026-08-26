using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidApiClient
{
    private readonly HttpClient _httpClient;
    private readonly PlaidOptions _options;

    public PlaidApiClient(
        HttpClient httpClient,
        IOptions<PlaidOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<JsonDocument> PostAsync(
        string endpoint,
        object payload,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var requestUri =
            new Uri(
                new Uri(
                    _options.BaseUrl),
                endpoint);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                requestUri);

        request.Headers.Add(
            "PLAID-CLIENT-ID",
            _options.ClientId);

        request.Headers.Add(
            "PLAID-SECRET",
            _options.Secret);

        request.Content =
            JsonContent.Create(
                payload);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        var responseText =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorMetadata =
                ReadSafeErrorMetadata(
                    responseText);

            throw new PlaidApiException(
                response.StatusCode,
                errorMetadata.ErrorType,
                errorMetadata.ErrorCode,
                errorMetadata.RequestId);
        }

        return JsonDocument.Parse(
            responseText);
    }

    private static PlaidErrorMetadata
        ReadSafeErrorMetadata(
            string responseText)
    {
        if (string.IsNullOrWhiteSpace(
                responseText))
        {
            return new PlaidErrorMetadata(
                null,
                null,
                null);
        }

        try
        {
            using var document =
                JsonDocument.Parse(
                    responseText);

            var root =
                document.RootElement;

            return new PlaidErrorMetadata(
                GetStringProperty(
                    root,
                    "error_type"),

                GetStringProperty(
                    root,
                    "error_code"),

                GetStringProperty(
                    root,
                    "request_id"));
        }
        catch (JsonException)
        {
            return new PlaidErrorMetadata(
                null,
                null,
                null);
        }
    }

    private static string? GetStringProperty(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var property))
        {
            return null;
        }

        if (property.ValueKind !=
            JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(
                _options.ClientId))
        {
            throw new InvalidOperationException(
                "Plaid ClientId is not configured.");
        }

        if (string.IsNullOrWhiteSpace(
                _options.Secret))
        {
            throw new InvalidOperationException(
                "Plaid Secret is not configured.");
        }
    }

    private sealed record PlaidErrorMetadata(
        string? ErrorType,
        string? ErrorCode,
        string? RequestId);
}