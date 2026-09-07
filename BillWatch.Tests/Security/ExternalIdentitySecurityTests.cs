using System.Net;
using System.Net.Http.Json;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Identity;
using BillWatch.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Security;

public sealed class ExternalIdentitySecurityTests
{
    private const string TestPassword =
        "BillWatch!Tests123";

    [Fact]
    public async Task ExternalLogin_UnconfiguredProvider_FailsClosed()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/login",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    idToken =
                        "not-a-real-provider-token"
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task ExternalLink_AnonymousCaller_IsRejected()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/link",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    idToken =
                        "not-a-real-provider-token",

                    currentPassword =
                        TestPassword,

                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task ExternalLink_AuthenticatedCallerWithoutPasswordReauthentication_IsRejected()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        TestUserAuthentication.Authorize(
            client,
            session);

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/link",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    idToken =
                        "not-a-real-provider-token",

                    currentPassword =
                        string.Empty,

                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task ExternalLink_TwoFactorAccountWithoutSecondFactor_IsRejected()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        await using (
            var scope =
                factory.Services.CreateAsyncScope())
        {
            var userManager =
                scope.ServiceProvider
                    .GetRequiredService<
                        UserManager<ApplicationUser>>();

            var user =
                await userManager.FindByEmailAsync(
                    session.Email);

            Assert.NotNull(user);

            var enableResult =
                await userManager.SetTwoFactorEnabledAsync(
                    user!,
                    true);

            Assert.True(
                enableResult.Succeeded);
        }

        TestUserAuthentication.Authorize(
            client,
            session);

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/link",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    idToken =
                        "not-a-real-provider-token",

                    currentPassword =
                        TestPassword,

                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task ExternalLink_AuthenticatedCallerWithInvalidProviderToken_FailsWithoutLinking()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        TestUserAuthentication.Authorize(
            client,
            session);

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/link",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    idToken =
                        "not-a-real-provider-token",

                    currentPassword =
                        TestPassword,

                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public void ConfigurationBinding_OnlyEnablesProvidersWithAnAudience()
    {
        var settings =
            new Dictionary<string, string?>
            {
                ["ExternalIdentity:Google:Audience"] =
                    "billwatch-google-client-id",

                ["ExternalIdentity:Apple:Audience"] =
                    string.Empty,

                ["ExternalIdentity:Microsoft:Audience"] =
                    string.Empty
            };

        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    settings)
                .Build();

        var validator =
            new ExternalIdentityTokenValidator(
                configuration,
                new TestHttpClientFactory());

        Assert.True(
            validator.IsProviderConfigured(
                ExternalIdentityProviders.Google));

        Assert.False(
            validator.IsProviderConfigured(
                ExternalIdentityProviders.Apple));

        Assert.False(
            validator.IsProviderConfigured(
                ExternalIdentityProviders.Microsoft));

        Assert.False(
            validator.IsProviderConfigured(
                "unknown-provider"));
    }

    private sealed class TestHttpClientFactory :
        IHttpClientFactory
    {
        public HttpClient CreateClient(
            string name)
        {
            return new HttpClient(
                new HttpClientHandler())
            {
                Timeout =
                    TimeSpan.FromSeconds(1)
            };
        }
    }
}
