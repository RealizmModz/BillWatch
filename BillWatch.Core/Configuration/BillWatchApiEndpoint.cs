using System.Net;

namespace BillWatch.Core.Configuration;

public static class BillWatchApiEndpoint
{
    public static Uri Parse(
        string? configuredValue,
        bool allowLocalDevelopmentEndpoint)
    {
        if (string.IsNullOrWhiteSpace(
                configuredValue))
        {
            throw new InvalidOperationException(
                "The BillWatch API base URL is not configured.");
        }

        if (!Uri.TryCreate(
                configuredValue.Trim(),
                UriKind.Absolute,
                out var endpoint))
        {
            throw new InvalidOperationException(
                "The BillWatch API base URL must be an absolute URL.");
        }

        if (!string.Equals(
                endpoint.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The BillWatch API base URL must use HTTPS.");
        }

        if (!string.IsNullOrEmpty(
                endpoint.UserInfo) ||
            !string.IsNullOrEmpty(
                endpoint.Query) ||
            !string.IsNullOrEmpty(
                endpoint.Fragment) ||
            !string.Equals(
                endpoint.AbsolutePath,
                "/",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The BillWatch API base URL must contain only an HTTPS origin.");
        }

        if (!allowLocalDevelopmentEndpoint &&
            IsLocalOrNumericHost(
                endpoint))
        {
            throw new InvalidOperationException(
                "A release build cannot use a local or numeric BillWatch API host.");
        }

        return new Uri(
            endpoint.GetLeftPart(
                UriPartial.Authority) +
            "/",
            UriKind.Absolute);
    }

    private static bool IsLocalOrNumericHost(
        Uri endpoint)
    {
        return endpoint.IsLoopback ||
               IPAddress.TryParse(
                   endpoint.Host,
                   out _) ||
               endpoint.Host.EndsWith(
                   ".local",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   endpoint.Host,
                   "host.docker.internal",
                   StringComparison.OrdinalIgnoreCase);
    }
}
