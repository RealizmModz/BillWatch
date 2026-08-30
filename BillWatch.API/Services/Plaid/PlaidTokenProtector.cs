using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace BillWatch.API.Services.Plaid;

public sealed class PlaidTokenProtector
{
    /*
     * Purpose strings are part of the cryptographic isolation boundary.
     *
     * Never reuse either protector for unrelated secrets. Versioning the
     * purpose also gives BillWatch a deliberate migration path if token
     * protection needs to change later.
     */
    private const string AccessTokenPurpose =
        "BillWatch.Plaid.AccessToken.v1";

    private const string LinkTokenPurpose =
        "BillWatch.Plaid.LinkToken.v1";

    /*
     * Plaid tokens are normally far smaller than these limits.
     *
     * The bounds protect Data Protection from accidentally processing
     * unreasonably large values originating from corrupted storage or a
     * future programming error.
     */
    private const int MaxPlaintextTokenLength =
        8 * 1024;

    private const int MaxProtectedTokenLength =
        64 * 1024;

    private readonly IDataProtector
        _accessTokenProtector;

    private readonly IDataProtector
        _linkTokenProtector;

    public PlaidTokenProtector(
        IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(
            dataProtectionProvider);

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
        return ProtectToken(
            _accessTokenProtector,
            accessToken,
            nameof(accessToken),
            "Plaid access token");
    }

    public string Unprotect(
        string protectedAccessToken)
    {
        return UnprotectToken(
            _accessTokenProtector,
            protectedAccessToken,
            nameof(protectedAccessToken),
            "protected Plaid access token");
    }

    public string ProtectLinkToken(
        string linkToken)
    {
        return ProtectToken(
            _linkTokenProtector,
            linkToken,
            nameof(linkToken),
            "Plaid Link token");
    }

    public string UnprotectLinkToken(
        string protectedLinkToken)
    {
        return UnprotectToken(
            _linkTokenProtector,
            protectedLinkToken,
            nameof(protectedLinkToken),
            "protected Plaid Link token");
    }

    private static string ProtectToken(
        IDataProtector protector,
        string token,
        string parameterName,
        string tokenDescription)
    {
        ArgumentNullException.ThrowIfNull(
            protector);

        ValidatePlaintextToken(
            token,
            parameterName,
            tokenDescription);

        /*
         * Do not trim or otherwise normalize opaque provider tokens.
         * Their exact byte-for-byte value is part of the credential.
         */
        var protectedToken =
            protector.Protect(
                token);

        if (string.IsNullOrWhiteSpace(
                protectedToken) ||
            protectedToken.Length >
                MaxProtectedTokenLength)
        {
            /*
             * This should never happen with a correctly functioning
             * Data Protection provider.
             *
             * The exception deliberately contains no token material.
             */
            throw new CryptographicException(
                "Plaid token protection produced an invalid protected payload.");
        }

        return protectedToken;
    }

    private static string UnprotectToken(
        IDataProtector protector,
        string protectedToken,
        string parameterName,
        string tokenDescription)
    {
        ArgumentNullException.ThrowIfNull(
            protector);

        ValidateProtectedToken(
            protectedToken,
            parameterName,
            tokenDescription);

        /*
         * IDataProtector fails authentication before returning tampered
         * ciphertext. CryptographicException is intentionally allowed to
         * propagate so callers can treat damaged or undecryptable stored
         * credentials as a security-sensitive failure.
         */
        var token =
            protector.Unprotect(
                protectedToken);

        if (string.IsNullOrWhiteSpace(
                token) ||
            token.Length >
                MaxPlaintextTokenLength)
        {
            throw new CryptographicException(
                "Protected Plaid token contained an invalid payload.");
        }

        return token;
    }

    private static void ValidatePlaintextToken(
        string token,
        string parameterName,
        string tokenDescription)
    {
        if (string.IsNullOrWhiteSpace(
                token))
        {
            throw new ArgumentException(
                $"{tokenDescription} cannot be empty.",
                parameterName);
        }

        if (token.Length >
            MaxPlaintextTokenLength)
        {
            throw new ArgumentException(
                $"{tokenDescription} exceeds the allowed length.",
                parameterName);
        }
    }

    private static void ValidateProtectedToken(
        string protectedToken,
        string parameterName,
        string tokenDescription)
    {
        if (string.IsNullOrWhiteSpace(
                protectedToken))
        {
            throw new ArgumentException(
                $"{tokenDescription} cannot be empty.",
                parameterName);
        }

        if (protectedToken.Length >
            MaxProtectedTokenLength)
        {
            throw new ArgumentException(
                $"{tokenDescription} exceeds the allowed length.",
                parameterName);
        }
    }
}