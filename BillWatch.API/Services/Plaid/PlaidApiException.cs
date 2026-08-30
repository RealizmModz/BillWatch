using System.Net;
using System.Text;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidApiException : Exception
{
    private const int MaxMetadataLength =
        128;

    public HttpStatusCode StatusCode { get; }

    public string? ErrorType { get; }

    public string? ErrorCode { get; }

    public string? RequestId { get; }

    /*
     * These values are deliberately limited to safe, machine-readable
     * metadata.
     *
     * Raw Plaid response bodies, credentials, access tokens, account data,
     * and arbitrary provider messages must never enter this exception.
     */
    public PlaidApiException(
        HttpStatusCode statusCode,
        string? errorType = null,
        string? errorCode = null,
        string? requestId = null)
        : this(
            statusCode,
            SanitizeMetadata(
                errorType),
            SanitizeMetadata(
                errorCode),
            SanitizeMetadata(
                requestId),
            metadataAlreadySanitized:
                true)
    {
    }

    private PlaidApiException(
        HttpStatusCode statusCode,
        string? errorType,
        string? errorCode,
        string? requestId,
        bool metadataAlreadySanitized)
        : base(
            BuildSafeMessage(
                statusCode,
                errorType,
                errorCode,
                requestId))
    {
        _ =
            metadataAlreadySanitized;

        StatusCode =
            statusCode;

        ErrorType =
            errorType;

        ErrorCode =
            errorCode;

        RequestId =
            requestId;
    }

    public bool IsTransient =>
        StatusCode is
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout ||
        (int)StatusCode >=
            500;

    private static string BuildSafeMessage(
        HttpStatusCode statusCode,
        string? errorType,
        string? errorCode,
        string? requestId)
    {
        var message =
            new StringBuilder();

        message.Append(
            "Plaid request failed with HTTP ");

        message.Append(
            (int)statusCode);

        message.Append(
            '.');

        if (errorType is not null)
        {
            message.Append(
                " Type: ");

            message.Append(
                errorType);

            message.Append(
                '.');
        }

        if (errorCode is not null)
        {
            message.Append(
                " Code: ");

            message.Append(
                errorCode);

            message.Append(
                '.');
        }

        if (requestId is not null)
        {
            message.Append(
                " Request ID: ");

            message.Append(
                requestId);

            message.Append(
                '.');
        }

        return message.ToString();
    }

    private static string? SanitizeMetadata(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        var trimmed =
            value.Trim();

        var builder =
            new StringBuilder(
                Math.Min(
                    trimmed.Length,
                    MaxMetadataLength));

        foreach (var character in
                 trimmed)
        {
            if (builder.Length >=
                MaxMetadataLength)
            {
                break;
            }

            /*
             * Plaid error types, error codes, and request IDs are expected
             * to be compact identifiers.
             *
             * Restricting this alphabet prevents control characters,
             * log-forging content, JSON fragments, URLs, token-shaped text,
             * and arbitrary upstream prose from entering application logs.
             */
            if (char.IsAsciiLetterOrDigit(
                    character) ||
                character is
                    '_' or
                    '-' or
                    '.')
            {
                builder.Append(
                    character);
            }
        }

        return builder.Length ==
            0
            ? null
            : builder.ToString();
    }
}