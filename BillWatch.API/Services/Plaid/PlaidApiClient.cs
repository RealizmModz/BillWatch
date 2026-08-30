using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidApiClient
{
    /*
     * Plaid responses used by BillWatch are JSON API payloads, not bulk
     * downloads. Bound the response before buffering it into memory.
     */
    private const long MaxResponseBytes =
        5L * 1024 * 1024;

    private readonly HttpClient _httpClient;

    private readonly PlaidOptions _options;

    public PlaidApiClient(
        HttpClient httpClient,
        IOptions<PlaidOptions> options)
    {
        ArgumentNullException.ThrowIfNull(
            httpClient);

        ArgumentNullException.ThrowIfNull(
            options);

        _httpClient =
            httpClient;

        _options =
            options.Value
            ?? throw new InvalidOperationException(
                "Plaid configuration is unavailable.");
    }

    public async Task<JsonDocument> PostAsync(
        string endpoint,
        object payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            endpoint);

        ArgumentNullException.ThrowIfNull(
            payload);

        cancellationToken.ThrowIfCancellationRequested();

        EnsureConfigured();

        var requestUri =
            CreateRequestUri(
                endpoint);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                requestUri);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        /*
         * Credentials belong only in outbound Plaid headers.
         *
         * Never include these values in exceptions, logs, response objects,
         * or application-visible DTOs.
         */
        request.Headers.TryAddWithoutValidation(
            "PLAID-CLIENT-ID",
            _options.ClientId.Trim());

        request.Headers.TryAddWithoutValidation(
            "PLAID-SECRET",
            _options.Secret.Trim());

        request.Content =
            JsonContent.Create(
                payload);

        using var response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        await EnsureResponseSizeIsAllowedAsync(
            response,
            cancellationToken);

        var responseText =
            await ReadBoundedResponseAsync(
                response,
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

        if (string.IsNullOrWhiteSpace(
                responseText))
        {
            throw new PlaidApiException(
                HttpStatusCode.BadGateway,
                errorType:
                    "INVALID_RESPONSE",
                errorCode:
                    "EMPTY_RESPONSE",
                requestId:
                    null);
        }

        try
        {
            return JsonDocument.Parse(
                responseText);
        }
        catch (JsonException)
        {
            /*
             * A successful HTTP status with malformed JSON is still an
             * invalid upstream response. Do not leak the raw body.
             */
            throw new PlaidApiException(
                HttpStatusCode.BadGateway,
                errorType:
                    "INVALID_RESPONSE",
                errorCode:
                    "INVALID_JSON",
                requestId:
                    null);
        }
    }

    private Uri CreateRequestUri(
        string endpoint)
    {
        var normalizedEndpoint =
            endpoint.Trim();

        /*
         * Callers may supply Plaid API paths only.
         *
         * Reject absolute/protocol-relative URLs so this client can never
         * become an SSRF primitive that forwards Plaid credentials to an
         * attacker-controlled host.
         */
        if (Uri.TryCreate(
                normalizedEndpoint,
                UriKind.Absolute,
                out _) ||
            normalizedEndpoint.StartsWith(
                "//",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Plaid endpoint must be a relative API path.",
                nameof(endpoint));
        }

        normalizedEndpoint =
            normalizedEndpoint.TrimStart(
                '/');

        if (normalizedEndpoint.Length ==
            0)
        {
            throw new ArgumentException(
                "Plaid endpoint cannot be empty.",
                nameof(endpoint));
        }

        if (normalizedEndpoint.Contains(
                '?',
                StringComparison.Ordinal) ||
            normalizedEndpoint.Contains(
                '#',
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Plaid endpoint must not contain a query string or fragment.",
                nameof(endpoint));
        }

        if (normalizedEndpoint
            .Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries)
            .Any(
                segment =>
                    segment is "." or ".."))
        {
            throw new ArgumentException(
                "Plaid endpoint contains an invalid path segment.",
                nameof(endpoint));
        }

        var baseUri =
            new Uri(
                _options.BaseUrl,
                UriKind.Absolute);

        var requestUri =
            new Uri(
                baseUri,
                normalizedEndpoint);

        /*
         * Defense in depth: even if PlaidOptions changes later, credentials
         * must only be sent to the configured HTTPS Plaid origin.
         */
        if (!string.Equals(
                requestUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                requestUri.Host,
                baseUri.Host,
                StringComparison.OrdinalIgnoreCase) ||
            requestUri.Port !=
                baseUri.Port)
        {
            throw new InvalidOperationException(
                "Plaid request URI resolved outside the configured Plaid origin.");
        }

        return requestUri;
    }

    private static async Task EnsureResponseSizeIsAllowedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            response);

        cancellationToken.ThrowIfCancellationRequested();

        var contentLength =
            response.Content.Headers.ContentLength;

        if (contentLength.HasValue &&
            contentLength.Value >
                MaxResponseBytes)
        {
            throw new PlaidApiException(
                HttpStatusCode.BadGateway,
                errorType:
                    "INVALID_RESPONSE",
                errorCode:
                    "RESPONSE_TOO_LARGE",
                requestId:
                    null);
        }

        await Task.CompletedTask;
    }

    private static async Task<string> ReadBoundedResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var responseStream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        using var buffer =
            new MemoryStream();

        var chunk =
            new byte[16 * 1024];

        while (true)
        {
            var bytesRead =
                await responseStream.ReadAsync(
                    chunk.AsMemory(
                        0,
                        chunk.Length),
                    cancellationToken);

            if (bytesRead ==
                0)
            {
                break;
            }

            if (buffer.Length +
                    bytesRead >
                MaxResponseBytes)
            {
                throw new PlaidApiException(
                    HttpStatusCode.BadGateway,
                    errorType:
                        "INVALID_RESPONSE",
                    errorCode:
                        "RESPONSE_TOO_LARGE",
                    requestId:
                        null);
            }

            await buffer.WriteAsync(
                chunk.AsMemory(
                    0,
                    bytesRead),
                cancellationToken);
        }

        buffer.Position =
            0;

        using var reader =
            new StreamReader(
                buffer,
                detectEncodingFromByteOrderMarks:
                    true);

        return await reader.ReadToEndAsync(
            cancellationToken);
    }

    private static PlaidErrorMetadata ReadSafeErrorMetadata(
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
                SanitizeMetadata(
                    GetStringProperty(
                        root,
                        "error_type")),

                SanitizeMetadata(
                    GetStringProperty(
                        root,
                        "error_code")),

                SanitizeMetadata(
                    GetStringProperty(
                        root,
                        "request_id")));
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
                out var property) ||
            property.ValueKind !=
                JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static string? SanitizeMetadata(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        /*
         * Plaid error identifiers should be short machine-readable values.
         * Bound them before allowing them into application exceptions.
         */
        var sanitized =
            new string(
                value
                    .Trim()
                    .Where(
                        character =>
                            char.IsLetterOrDigit(
                                character) ||
                            character is
                                '_' or
                                '-' or
                                '.')
                    .Take(
                        128)
                    .ToArray());

        return sanitized.Length ==
            0
            ? null
            : sanitized;
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

        /*
         * Accessing BaseUrl deliberately validates the environment using the
         * fail-closed PlaidOptions implementation.
         */
        _ =
            _options.BaseUrl;
    }

    private sealed record PlaidErrorMetadata(
        string? ErrorType,
        string? ErrorCode,
        string? RequestId);
}