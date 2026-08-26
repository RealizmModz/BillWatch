using System.Net;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public string? ErrorType { get; }

    public string? ErrorCode { get; }

    public string? RequestId { get; }

    public PlaidApiException(
        HttpStatusCode statusCode,
        string? errorType = null,
        string? errorCode = null,
        string? requestId = null)
        : base(
            BuildSafeMessage(
                statusCode,
                errorType,
                errorCode,
                requestId))
    {
        StatusCode = statusCode;
        ErrorType = Clean(errorType);
        ErrorCode = Clean(errorCode);
        RequestId = Clean(requestId);
    }

    private static string BuildSafeMessage(
        HttpStatusCode statusCode,
        string? errorType,
        string? errorCode,
        string? requestId)
    {
        var parts =
            new List<string>
            {
                $"Plaid request failed with HTTP {(int)statusCode}."
            };

        var cleanErrorType =
            Clean(errorType);

        var cleanErrorCode =
            Clean(errorCode);

        var cleanRequestId =
            Clean(requestId);

        if (cleanErrorType is not null)
        {
            parts.Add(
                $"Type: {cleanErrorType}.");
        }

        if (cleanErrorCode is not null)
        {
            parts.Add(
                $"Code: {cleanErrorCode}.");
        }

        if (cleanRequestId is not null)
        {
            parts.Add(
                $"Request ID: {cleanRequestId}.");
        }

        return string.Join(
            ' ',
            parts);
    }

    private static string? Clean(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Replace(
                "\r",
                string.Empty,
                StringComparison.Ordinal)
            .Replace(
                "\n",
                string.Empty,
                StringComparison.Ordinal)
            .Trim();
    }
}