using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Identity;
using BillWatch.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Security;

public sealed class ExternalIdentitySecondFactorVerifierTests
{
    [Fact]
    public async Task VerifyAsync_TwoFactorDisabled_DoesNotRequireCode()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        await using var scope =
            factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider.GetRequiredService<
                UserManager<ApplicationUser>>();

        var user =
            await userManager.FindByEmailAsync(
                session.Email);

        Assert.NotNull(user);

        var verifier =
            new ExternalIdentitySecondFactorVerifier(
                userManager);

        var result =
            await verifier.VerifyAsync(
                user!,
                authenticatorCode: null,
                recoveryCode: null);

        Assert.Equal(
            ExternalIdentitySecondFactorResult.NotRequired,
            result);
    }

    [Fact]
    public async Task VerifyAsync_TwoFactorEnabledWithoutCode_RequiresSecondFactor()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        await using var scope =
            factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider.GetRequiredService<
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

        var verifier =
            new ExternalIdentitySecondFactorVerifier(
                userManager);

        var result =
            await verifier.VerifyAsync(
                user!,
                authenticatorCode: null,
                recoveryCode: null);

        Assert.Equal(
            ExternalIdentitySecondFactorResult.Required,
            result);
    }

    [Fact]
    public async Task VerifyAsync_RecoveryCode_IsSingleUse()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        await using var scope =
            factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider.GetRequiredService<
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

        var recoveryCodes =
            await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                user!,
                2);

        var recoveryCode =
            recoveryCodes!.First();

        var verifier =
            new ExternalIdentitySecondFactorVerifier(
                userManager);

        var firstResult =
            await verifier.VerifyAsync(
                user!,
                authenticatorCode: null,
                recoveryCode: recoveryCode);

        var secondResult =
            await verifier.VerifyAsync(
                user!,
                authenticatorCode: null,
                recoveryCode: recoveryCode);

        Assert.Equal(
            ExternalIdentitySecondFactorResult.Succeeded,
            firstResult);

        Assert.Equal(
            ExternalIdentitySecondFactorResult.Failed,
            secondResult);
    }

    [Fact]
    public async Task VerifyAsync_BothCodeTypes_FailsWithoutRedeemingRecoveryCode()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        await using var scope =
            factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider.GetRequiredService<
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

        var recoveryCodes =
            await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                user!,
                1);

        var recoveryCode =
            recoveryCodes!.Single();

        var verifier =
            new ExternalIdentitySecondFactorVerifier(
                userManager);

        var ambiguousResult =
            await verifier.VerifyAsync(
                user!,
                authenticatorCode: "123456",
                recoveryCode: recoveryCode);

        var recoveryResult =
            await verifier.VerifyAsync(
                user!,
                authenticatorCode: null,
                recoveryCode: recoveryCode);

        Assert.Equal(
            ExternalIdentitySecondFactorResult.Failed,
            ambiguousResult);

        Assert.Equal(
            ExternalIdentitySecondFactorResult.Succeeded,
            recoveryResult);
    }
}
