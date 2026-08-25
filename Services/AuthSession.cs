namespace BillWatch.Services;

public sealed class AuthSession
{
    private const string AccessTokenKey = "billwatch_access_token";
    private const string RefreshTokenKey = "billwatch_refresh_token";

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
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        return await SecureStorage.Default.GetAsync(
            AccessTokenKey);
    }

    public async Task<string?> GetRefreshTokenAsync()
    {
        return await SecureStorage.Default.GetAsync(
            RefreshTokenKey);
    }

    public void Clear()
    {
        SecureStorage.Default.Remove(AccessTokenKey);
        SecureStorage.Default.Remove(RefreshTokenKey);
    }
}