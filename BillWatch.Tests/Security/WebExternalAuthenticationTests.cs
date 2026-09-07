using System.Net;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class WebExternalAuthenticationTests
{
    [Fact]
    public async Task Login_HidesUnconfiguredExternalProviders()
    {
        using var factory =
            new BillWatchWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/login");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var body =
            await response.Content
                .ReadAsStringAsync();

        Assert.DoesNotContain(
            "/auth/external/google",
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "/auth/external/apple",
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "/auth/external/microsoft",
            body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountSettings_HidesUnconfiguredExternalProviderLinks()
    {
        using var factory =
            new BillWatchWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/app/account/settings");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var body =
            await response.Content
                .ReadAsStringAsync();

        Assert.DoesNotContain(
            "/auth/external/google/link",
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "/auth/external/apple/link",
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "/auth/external/microsoft/link",
            body,
            StringComparison.Ordinal);
    }

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
    public async Task UnconfiguredKnownProvider_LinkFailsClosedBackToSettings()
    {
        using var factory =
            new BillWatchWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/auth/external/google/link");

        Assert.Equal(
            HttpStatusCode.Redirect,
            response.StatusCode);

        Assert.NotNull(
            response.Headers.Location);

        Assert.StartsWith(
            "/app/account/settings?externalError=",
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
    public async Task UnknownProvider_LinkReturnsNotFound()
    {
        using var factory =
            new BillWatchWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/auth/external/not-a-provider/link");

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
