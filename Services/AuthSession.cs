using System.Globalization;

namespace BillWatch.Services;

public sealed class AuthSession
{
    private const string AccessTokenKey =
        "billwatch_access_token";

    private const string RefreshTokenKey =
        "billwatch_refresh_token";

    private const string AccessTokenExpiresAtKey =
        "billwatch_access_token_expires_at";

    public async Task SaveAsync(
        string accessToken,
        string refreshToken,
        long expiresInSeconds)
    {
        if (string.IsNullOrWhiteSpace(
                accessToken))
        {
            throw new ArgumentException(
                "Access token is required.",
                nameof(accessToken));
        }

        if (string.IsNullOrWhiteSpace(
                refreshToken))
        {
            throw new ArgumentException(
                "Refresh token is required.",
                nameof(refreshToken));
        }

        var expiresAtUtc =
            DateTimeOffset.UtcNow.AddSeconds(
                Math.Max(
                    expiresInSeconds,
                    0));

        await SecureStorage.Default.SetAsync(
            AccessTokenKey,
            accessToken);

        await SecureStorage.Default.SetAsync(
            RefreshTokenKey,
            refreshToken);

        await SecureStorage.Default.SetAsync(
            AccessTokenExpiresAtKey,
            expiresAtUtc.ToString(
                "O",
                CultureInfo.InvariantCulture));
    }

    public async Task SaveAsync(
        string accessToken,
        string refreshToken)
    {
        await SecureStorage.Default.SetAsync(
            AccessTokenKey,
            accessToken);

        await SecureStorage.Default.SetAsync(
            RefreshTokenKey,
            refreshToken);

        SecureStorage.Default.Remove(
            AccessTokenExpiresAtKey);
    }

    public async Task<string?>
        GetAccessTokenAsync()
    {
        return await SecureStorage.Default
            .GetAsync(
                AccessTokenKey);
    }

    public async Task<string?>
        GetRefreshTokenAsync()
    {
        return await SecureStorage.Default
            .GetAsync(
                RefreshTokenKey);
    }

    public async Task<DateTimeOffset?>
        GetAccessTokenExpiresAtUtcAsync()
    {
        var value =
            await SecureStorage.Default
                .GetAsync(
                    AccessTokenExpiresAtKey);

        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var expiresAtUtc))
        {
            return null;
        }

        return expiresAtUtc;
    }

    public async Task<bool>
        HasRefreshTokenAsync()
    {
        var refreshToken =
            await GetRefreshTokenAsync();

        return !string.IsNullOrWhiteSpace(
            refreshToken);
    }

    public void Clear()
    {
        SecureStorage.Default.Remove(
            AccessTokenKey);

        SecureStorage.Default.Remove(
            RefreshTokenKey);

        SecureStorage.Default.Remove(
            AccessTokenExpiresAtKey);
    }
}