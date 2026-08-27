using System.Net;

namespace BillWatch.Services;

public sealed class AuthenticationService
{
    private static readonly TimeSpan
        RefreshBeforeExpiration =
            TimeSpan.FromMinutes(1);

    private readonly BillWatchApiClient
        _apiClient;

    private readonly AuthSession
        _authSession;

    private readonly SemaphoreSlim
        _refreshLock =
            new(1, 1);

    public AuthenticationService(
        BillWatchApiClient apiClient,
        AuthSession authSession)
    {
        _apiClient =
            apiClient;

        _authSession =
            authSession;
    }

    public event EventHandler?
        SessionExpired;

    public async Task LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var result =
            await _apiClient.LoginAsync(
                email,
                password,
                cancellationToken);

        await SaveTokenResultAsync(
            result);
    }

    public async Task<string>
        GetValidAccessTokenAsync(
            CancellationToken cancellationToken = default)
    {
        var accessToken =
            await _authSession
                .GetAccessTokenAsync();

        var expiresAtUtc =
            await _authSession
                .GetAccessTokenExpiresAtUtcAsync();

        if (!string.IsNullOrWhiteSpace(
                accessToken) &&
            expiresAtUtc.HasValue &&
            expiresAtUtc.Value >
                DateTimeOffset.UtcNow +
                RefreshBeforeExpiration)
        {
            return accessToken;
        }

        return await RefreshAccessTokenAsync(
            cancellationToken);
    }

    public async Task<bool>
        IsAuthenticatedAsync(
            CancellationToken cancellationToken = default)
    {
        var accessToken =
            await _authSession
                .GetAccessTokenAsync();

        var refreshToken =
            await _authSession
                .GetRefreshTokenAsync();

        if (string.IsNullOrWhiteSpace(
                accessToken) &&
            string.IsNullOrWhiteSpace(
                refreshToken))
        {
            return false;
        }

        try
        {
            await GetValidAccessTokenAsync(
                cancellationToken);

            return true;
        }
        catch (SessionExpiredException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return !string.IsNullOrWhiteSpace(
                refreshToken);
        }
    }

    public void Logout()
    {
        _authSession.Clear();

        SessionExpired?.Invoke(
            this,
            EventArgs.Empty);
    }

    private async Task<string>
        RefreshAccessTokenAsync(
            CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(
            cancellationToken);

        try
        {
            var currentAccessToken =
                await _authSession
                    .GetAccessTokenAsync();

            var currentExpiresAtUtc =
                await _authSession
                    .GetAccessTokenExpiresAtUtcAsync();

            if (!string.IsNullOrWhiteSpace(
                    currentAccessToken) &&
                currentExpiresAtUtc.HasValue &&
                currentExpiresAtUtc.Value >
                    DateTimeOffset.UtcNow +
                    RefreshBeforeExpiration)
            {
                return currentAccessToken;
            }

            var refreshToken =
                await _authSession
                    .GetRefreshTokenAsync();

            if (string.IsNullOrWhiteSpace(
                    refreshToken))
            {
                ExpireSession();

                throw new SessionExpiredException();
            }

            LoginResult result;

            try
            {
                result =
                    await _apiClient
                        .RefreshAccessTokenAsync(
                            refreshToken,
                            cancellationToken);
            }
            catch (HttpRequestException exception)
                when (exception.StatusCode is
                    HttpStatusCode.BadRequest or
                    HttpStatusCode.Unauthorized or
                    HttpStatusCode.Forbidden)
            {
                ExpireSession();

                throw new SessionExpiredException(
                    exception);
            }

            await SaveTokenResultAsync(
                result);

            return result.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task SaveTokenResultAsync(
        LoginResult result)
    {
        await _authSession.SaveAsync(
            result.AccessToken,
            result.RefreshToken,
            result.ExpiresIn);
    }

    private void ExpireSession()
    {
        _authSession.Clear();

        SessionExpired?.Invoke(
            this,
            EventArgs.Empty);
    }
}

public sealed class SessionExpiredException :
    Exception
{
    public SessionExpiredException()
        : base(
            "Your BillWatch session has expired.")
    {
    }

    public SessionExpiredException(
        Exception innerException)
        : base(
            "Your BillWatch session has expired.",
            innerException)
    {
    }
}