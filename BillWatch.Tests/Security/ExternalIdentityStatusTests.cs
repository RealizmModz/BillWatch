using System.Net;
using System.Net.Http.Json;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Identity;
using BillWatch.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Security;

public sealed class ExternalIdentityStatusTests
{
    [Fact]
    public async Task Status_AnonymousCaller_IsRejected()
    {
        using var factory = new BillWatchApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response =
            await client.GetAsync("/api/auth/external");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task Status_AuthenticatedUser_ReturnsOnlyTheirSupportedLinkedProviders()
    {
        using var factory = new BillWatchApiFactory();
        using var client = factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(client);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager =
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var user = await userManager.FindByEmailAsync(session.Email);
            Assert.NotNull(user);

            var googleResult = await userManager.AddLoginAsync(
                user!,
                new UserLoginInfo(
                    ExternalIdentityProviders.Google,
                    "test-google-subject",
                    "Google"));

            Assert.True(googleResult.Succeeded);

            var unsupportedResult = await userManager.AddLoginAsync(
                user!,
                new UserLoginInfo(
                    "unsupported-provider",
                    "test-unsupported-subject",
                    "Unsupported"));

            Assert.True(unsupportedResult.Succeeded);
        }

        TestUserAuthentication.Authorize(client, session);

        using var response =
            await client.GetAsync("/api/auth/external");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload =
            await response.Content.ReadFromJsonAsync<ExternalIdentityStatusResponse>();

        Assert.NotNull(payload);
        var provider = Assert.Single(payload!.LinkedProviders);
        Assert.Equal(ExternalIdentityProviders.Google, provider.Provider);
        Assert.Equal("Google", provider.DisplayName);
    }

    [Fact]
    public async Task Status_DoesNotExposeProviderSubjectIdentifiers()
    {
        using var factory = new BillWatchApiFactory();
        using var client = factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(client);

        const string sensitiveProviderSubject =
            "provider-subject-must-not-leave-api";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager =
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var user = await userManager.FindByEmailAsync(session.Email);
            Assert.NotNull(user);

            var result = await userManager.AddLoginAsync(
                user!,
                new UserLoginInfo(
                    ExternalIdentityProviders.Microsoft,
                    sensitiveProviderSubject,
                    "Microsoft"));

            Assert.True(result.Succeeded);
        }

        TestUserAuthentication.Authorize(client, session);

        using var response =
            await client.GetAsync("/api/auth/external");

        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(
            sensitiveProviderSubject,
            body,
            StringComparison.Ordinal);
    }
}
