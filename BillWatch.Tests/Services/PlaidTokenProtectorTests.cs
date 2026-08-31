using System.Security.Cryptography;
using BillWatch.API.Services.Plaid;
using Microsoft.AspNetCore.DataProtection;

namespace BillWatch.Tests.Services;

public sealed class PlaidTokenProtectorTests
{
    [Fact]
    public void AccessToken_RoundTripsWithoutNormalizingOpaqueValue()
    {
        var protector =
            CreateProtector();

        const string token =
            " access-token-with-significant-whitespace ";

        var protectedToken =
            protector.Protect(
                token);

        Assert.NotEqual(
            token,
            protectedToken);

        Assert.Equal(
            token,
            protector.Unprotect(
                protectedToken));
    }

    [Fact]
    public void AccessAndLinkTokenPurposes_AreCryptographicallyIsolated()
    {
        var protector =
            CreateProtector();

        var protectedAccessToken =
            protector.Protect(
                "access-token");

        Assert.Throws<CryptographicException>(
            () =>
                protector.UnprotectLinkToken(
                    protectedAccessToken));
    }

    [Fact]
    public void TamperedProtectedToken_IsRejected()
    {
        var protector =
            CreateProtector();

        var protectedToken =
            protector.ProtectLinkToken(
                "link-token");

        var replacementCharacter =
            protectedToken[^1] ==
                'A'
                ? 'B'
                : 'A';

        var tamperedToken =
            protectedToken[..^1] +
            replacementCharacter;

        Assert.Throws<CryptographicException>(
            () =>
                protector.UnprotectLinkToken(
                    tamperedToken));
    }

    private static PlaidTokenProtector CreateProtector()
    {
        return new PlaidTokenProtector(
            new EphemeralDataProtectionProvider());
    }
}
