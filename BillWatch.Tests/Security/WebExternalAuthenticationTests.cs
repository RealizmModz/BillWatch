using System.Net;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class WebExternalAuthenticationTests
{
    [Fact]
    public async Task UnconfiguredKnownProvider_FailsClosedWithoutStartingChallenge()
    {
        using var factory =
            new BillWatchWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/auth/external/google");

        Assert.Equal(
            HttpStatusCode.Redirect,
            response.StatusCode);

        Assert.NotNull(
            response.Headers.Location);

        Assert.StartsWith(
            "/login?error=",
            response.Headers.Location!.OriginalString,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownProvider_ReturnsNotFound()
    {
        using var factory =
            new BillWatchWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/auth/external/not-a-provider");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task CompletionWithoutTemporaryExternalSession_FailsClosed()
    {
        using var factory =
            new BillWatchWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/auth/external/complete?provider=google");

        Assert.Equal(
            HttpStatusCode.Redirect,
            response.StatusCode);

        Assert.NotNull(
            response.Headers.Location);

        Assert.StartsWith(
            "/login?error=",
            response.Headers.Location!.OriginalString,
            StringComparison.Ordinal);
    }
}
