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
    public async Task Login_ExternalErrorUsesFixedCopyInsteadOfEchoingErrorQuery()
    {
        using var factory =
            new BillWatchWebFactory();

        using var client =
            factory.CreateHttpsClient();

        const string spoofedMessage =
            "attacker-controlled-message-must-not-render";

        using var response =
            await client.GetAsync(
                "/login?externalError=true&error=" +
                spoofedMessage);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var body =
            await response.Content
                .ReadAsStringAsync();

        Assert.Contains(
            "BillWatch could not complete that external sign-in.",
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            spoofedMessage,
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
    public async Task AccountSettings_ExternalErrorUsesFixedCopyInsteadOfEchoingQuery()
    {
        using var factory =
            new BillWatchWebFactory();

        using var client =
            factory.CreateHttpsClient();

        const string spoofedMessage =
            "attacker-controlled-message-must-not-render";

        using var response =
            await client.GetAsync(
                "/app/account/settings?externalError=" +
                spoofedMessage);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var body =
            await response.Content
                .ReadAsStringAsync();

        Assert.Contains(
            "BillWatch could not complete that sign-in method.",
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            spoofedMessage,
            body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountSecurityScript_UsesSafeLinkedProviderStatusSurface()
    {
        using var factory =
            new BillWatchWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/js/account-security.js");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var body =
            await response.Content.ReadAsStringAsync();

        Assert.Contains(
            "/bff/account/external",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "linkedProviders",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "aria-disabled",
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "providerKey",
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "subject",
            body,
            StringComparison.OrdinalIgnoreCase);
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

        Assert.Equal(
            "/login?externalError=true",
            response.Headers.Location!.OriginalString);
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

        Assert.Equal(
            "/app/account/settings?externalError=true",
            response.Headers.Location!.OriginalString);
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

        Assert.Equal(
            "/login?externalError=true",
            response.Headers.Location!.OriginalString);
    }
}
