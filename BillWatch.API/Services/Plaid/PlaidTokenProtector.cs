using Microsoft.AspNetCore.DataProtection;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidTokenProtector
{
    private const string AccessTokenPurpose =
        "BillWatch.Plaid.AccessToken.v1";

    private const string LinkTokenPurpose =
        "BillWatch.Plaid.LinkToken.v1";

    private readonly IDataProtector _accessTokenProtector;
    private readonly IDataProtector _linkTokenProtector;

    public PlaidTokenProtector(
        IDataProtectionProvider dataProtectionProvider)
    {
        _accessTokenProtector =
            dataProtectionProvider.CreateProtector(
                AccessTokenPurpose);

        _linkTokenProtector =
            dataProtectionProvider.CreateProtector(
                LinkTokenPurpose);
    }

    public string Protect(
        string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException(
                "Plaid access token cannot be empty.",
                nameof(accessToken));
        }

        return _accessTokenProtector.Protect(
            accessToken);
    }

    public string Unprotect(
        string protectedAccessToken)
    {
        if (string.IsNullOrWhiteSpace(protectedAccessToken))
        {
            throw new ArgumentException(
                "Protected Plaid access token cannot be empty.",
                nameof(protectedAccessToken));
        }

        return _accessTokenProtector.Unprotect(
            protectedAccessToken);
    }

    public string ProtectLinkToken(
        string linkToken)
    {
        if (string.IsNullOrWhiteSpace(linkToken))
        {
            throw new ArgumentException(
                "Plaid Link token cannot be empty.",
                nameof(linkToken));
        }

        return _linkTokenProtector.Protect(
            linkToken);
    }

    public string UnprotectLinkToken(
        string protectedLinkToken)
    {
        if (string.IsNullOrWhiteSpace(protectedLinkToken))
        {
            throw new ArgumentException(
                "Protected Plaid Link token cannot be empty.",
                nameof(protectedLinkToken));
        }

        return _linkTokenProtector.Unprotect(
            protectedLinkToken);
    }
}