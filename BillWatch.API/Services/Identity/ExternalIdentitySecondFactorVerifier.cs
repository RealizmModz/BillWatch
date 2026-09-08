using BillWatch.API.Data.Entities;
using Microsoft.AspNetCore.Identity;

namespace BillWatch.API.Services.Identity;

public sealed class ExternalIdentitySecondFactorVerifier(
    UserManager<ApplicationUser> userManager)
{
    private readonly UserManager<ApplicationUser>
        _userManager =
            userManager ??
            throw new ArgumentNullException(
                nameof(userManager));

    public async Task<ExternalIdentitySecondFactorResult>
        VerifyAsync(
            ApplicationUser user,
            string? authenticatorCode,
            string? recoveryCode)
    {
        ArgumentNullException.ThrowIfNull(
            user);

        if (!await _userManager.GetTwoFactorEnabledAsync(
                user))
        {
            return ExternalIdentitySecondFactorResult
                .NotRequired;
        }

        authenticatorCode =
            NormalizeOptionalCode(
                authenticatorCode);

        recoveryCode =
            NormalizeOptionalCode(
                recoveryCode);

        if (authenticatorCode is null &&
            recoveryCode is null)
        {
            return ExternalIdentitySecondFactorResult
                .Required;
        }

        if (authenticatorCode is not null &&
            recoveryCode is not null)
        {
            return ExternalIdentitySecondFactorResult
                .Failed;
        }

        if (authenticatorCode is not null)
        {
            var validAuthenticatorCode =
                await _userManager.VerifyTwoFactorTokenAsync(
                    user,
                    _userManager.Options.Tokens.AuthenticatorTokenProvider,
                    NormalizeAuthenticatorCode(
                        authenticatorCode));

            return validAuthenticatorCode
                ? ExternalIdentitySecondFactorResult.Succeeded
                : ExternalIdentitySecondFactorResult.Failed;
        }

        var recoveryResult =
            await _userManager.RedeemTwoFactorRecoveryCodeAsync(
                user,
                recoveryCode!);

        return recoveryResult.Succeeded
            ? ExternalIdentitySecondFactorResult.Succeeded
            : ExternalIdentitySecondFactorResult.Failed;
    }

    private static string?
        NormalizeOptionalCode(
            string? value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? null
            : value.Trim();
    }

    private static string NormalizeAuthenticatorCode(
        string code)
    {
        return code
            .Replace(
                " ",
                string.Empty,
                StringComparison.Ordinal)
            .Replace(
                "-",
                string.Empty,
                StringComparison.Ordinal);
    }
}

public enum ExternalIdentitySecondFactorResult
{
    NotRequired = 0,
    Required = 1,
    Succeeded = 2,
    Failed = 3
}
