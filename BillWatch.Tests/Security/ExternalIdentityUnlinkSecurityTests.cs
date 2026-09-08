using System.Net;
using System.Net.Http.Json;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Identity;
using BillWatch.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Security;

public sealed class ExternalIdentityUnlinkSecurityTests
{
    private const string TestPassword =
        "BillWatch!Tests123";

    [Fact]
    public async Task Unlink_AnonymousCaller_IsRejected()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/unlink",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    currentPassword =
                        TestPassword,

                    twoFactorCode =
                        (string?)null,

                    twoFactorRecoveryCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task Unlink_WrongPassword_IsRejectedWithoutRemovingLogin()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        await AddExternalLoginAsync(
            factory,
            session.Email,
            ExternalIdentityProviders.Google,
            "google-subject");

        TestUserAuthentication.Authorize(
            client,
            session);

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/unlink",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    currentPassword =
                        "definitely-wrong-password",

                    twoFactorCode =
                        (string?)null,

                    twoFactorRecoveryCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.True(
            await HasExternalLoginAsync(
                factory,
                session.Email,
                ExternalIdentityProviders.Google));
    }

    [Fact]
    public async Task Unlink_TwoFactorAccountWithoutSecondFactor_IsRejectedWithoutRemovingLogin()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        await AddExternalLoginAsync(
            factory,
            session.Email,
            ExternalIdentityProviders.Google,
            "google-subject");

        await SetTwoFactorEnabledAsync(
            factory,
            session.Email);

        TestUserAuthentication.Authorize(
            client,
            session);

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/unlink",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    currentPassword =
                        TestPassword,

                    twoFactorCode =
                        (string?)null,

                    twoFactorRecoveryCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.True(
            await HasExternalLoginAsync(
                factory,
                session.Email,
                ExternalIdentityProviders.Google));
    }

    [Fact]
    public async Task Unlink_TwoFactorAccountWithRecoveryCode_RemovesLoginAndConsumesCode()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        await AddExternalLoginAsync(
            factory,
            session.Email,
            ExternalIdentityProviders.Google,
            "google-subject");

        var recoveryCode =
            await EnableTwoFactorAndCreateRecoveryCodeAsync(
                factory,
                session.Email);

        TestUserAuthentication.Authorize(
            client,
            session);

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/unlink",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    currentPassword =
                        TestPassword,

                    twoFactorCode =
                        (string?)null,

                    twoFactorRecoveryCode =
                        recoveryCode
                });

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);

        Assert.False(
            await HasExternalLoginAsync(
                factory,
                session.Email,
                ExternalIdentityProviders.Google));

        await AddExternalLoginAsync(
            factory,
            session.Email,
            ExternalIdentityProviders.Google,
            "google-subject-relinked");

        using var replayResponse =
            await client.PostAsJsonAsync(
                "/api/auth/external/unlink",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    currentPassword =
                        TestPassword,

                    twoFactorCode =
                        (string?)null,

                    twoFactorRecoveryCode =
                        recoveryCode
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            replayResponse.StatusCode);

        Assert.True(
            await HasExternalLoginAsync(
                factory,
                session.Email,
                ExternalIdentityProviders.Google));
    }

    [Fact]
    public async Task Unlink_ReauthenticatedCaller_RemovesOnlyRequestedLogin()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        await AddExternalLoginAsync(
            factory,
            session.Email,
            ExternalIdentityProviders.Google,
            "google-subject");

        await AddExternalLoginAsync(
            factory,
            session.Email,
            ExternalIdentityProviders.Microsoft,
            "microsoft-subject");

        TestUserAuthentication.Authorize(
            client,
            session);

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/unlink",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    currentPassword =
                        TestPassword,

                    twoFactorCode =
                        (string?)null,

                    twoFactorRecoveryCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);

        Assert.False(
            await HasExternalLoginAsync(
                factory,
                session.Email,
                ExternalIdentityProviders.Google));

        Assert.True(
            await HasExternalLoginAsync(
                factory,
                session.Email,
                ExternalIdentityProviders.Microsoft));
    }

    private static async Task AddExternalLoginAsync(
        BillWatchApiFactory factory,
        string email,
        string provider,
        string subject)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider.GetRequiredService<
                UserManager<ApplicationUser>>();

        var user =
            await userManager.FindByEmailAsync(
                email);

        Assert.NotNull(user);

        var result =
            await userManager.AddLoginAsync(
                user!,
                new UserLoginInfo(
                    provider,
                    subject,
                    provider));

        Assert.True(
            result.Succeeded);
    }

    private static async Task<bool> HasExternalLoginAsync(
        BillWatchApiFactory factory,
        string email,
        string provider)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider.GetRequiredService<
                UserManager<ApplicationUser>>();

        var user =
            await userManager.FindByEmailAsync(
                email);

        Assert.NotNull(user);

        var logins =
            await userManager.GetLoginsAsync(
                user!);

        return logins.Any(
            login => string.Equals(
                login.LoginProvider,
                provider,
                StringComparison.OrdinalIgnoreCase));
    }

    private static async Task SetTwoFactorEnabledAsync(
        BillWatchApiFactory factory,
        string email)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider.GetRequiredService<
                UserManager<ApplicationUser>>();

        var user =
            await userManager.FindByEmailAsync(
                email);

        Assert.NotNull(user);

        var result =
            await userManager.SetTwoFactorEnabledAsync(
                user!,
                true);

        Assert.True(
            result.Succeeded);
    }

    private static async Task<string> EnableTwoFactorAndCreateRecoveryCodeAsync(
        BillWatchApiFactory factory,
        string email)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider.GetRequiredService<
                UserManager<ApplicationUser>>();

        var user =
            await userManager.FindByEmailAsync(
                email);

        Assert.NotNull(user);

        var enableResult =
            await userManager.SetTwoFactorEnabledAsync(
                user!,
                true);

        Assert.True(
            enableResult.Succeeded);

        var recoveryCodes =
            await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                user!,
                1);

        var recoveryCode =
            recoveryCodes?.SingleOrDefault();

        Assert.False(
            string.IsNullOrWhiteSpace(
                recoveryCode));

        return recoveryCode!;
    }
}
