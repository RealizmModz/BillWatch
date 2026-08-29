using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BillWatch.Services;

public sealed class AuthenticationService
{
    private static readonly TimeSpan
        RefreshBeforeExpiration =
            TimeSpan.FromMinutes(
                1);

    private readonly BillWatchApiClient
        _apiClient;

    private readonly AuthSession
        _authSession;

    private readonly HttpClient
        _httpClient;

    private readonly SemaphoreSlim
        _refreshLock =
            new(
                1,
                1);

    public AuthenticationService(
        BillWatchApiClient apiClient,
        AuthSession authSession,
        HttpClient httpClient)
    {
        _apiClient =
            apiClient;

        _authSession =
            authSession;

        _httpClient =
            httpClient;
    }

    public event EventHandler?
        SessionExpired;

    public async Task RegisterAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                email))
        {
            throw new ArgumentException(
                "Email is required.",
                nameof(email));
        }

        if (string.IsNullOrWhiteSpace(
                password))
        {
            throw new ArgumentException(
                "Password is required.",
                nameof(password));
        }

        using var response =
            await _httpClient
                .PostAsJsonAsync(
                    "/api/auth/register",
                    new
                    {
                        email =
                            email.Trim(),

                        password
                    },
                    cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode ==
            HttpStatusCode.TooManyRequests)
        {
            throw new AccountRegistrationException(
                "Too many account requests. Wait a moment and try again.");
        }

        if (response.StatusCode ==
            HttpStatusCode.BadRequest)
        {
            var responseBody =
                await response.Content
                    .ReadAsStringAsync(
                        cancellationToken);

            if (responseBody.Contains(
                    "DuplicateEmail",
                    StringComparison.OrdinalIgnoreCase) ||
                responseBody.Contains(
                    "DuplicateUserName",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new AccountRegistrationException(
                    "An account with that email already exists. Sign in instead.");
            }

            throw new AccountRegistrationException(
                "BillWatch could not create the account. Use a valid email and a password with at least 12 characters, including uppercase, lowercase, a number, and a symbol.");
        }

        response.EnsureSuccessStatusCode();
    }

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

    public async Task RegisterAndLoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        await RegisterAsync(
            email,
            password,
            cancellationToken);

        await LoginAsync(
            email,
            password,
            cancellationToken);
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
            /*
             * Preserve a refresh token through a temporary network
             * outage. Network failure is not proof that the session
             * itself is invalid.
             */
            return !string.IsNullOrWhiteSpace(
                refreshToken);
        }
    }

    public async Task DeleteAccountAsync(
        CancellationToken cancellationToken = default)
    {
        var accessToken =
            await GetValidAccessTokenAsync(
                cancellationToken);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Delete,
                "/api/account");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            ExpireSession();

            return;
        }

        if (response.StatusCode is
            HttpStatusCode.Unauthorized or
            HttpStatusCode.Forbidden)
        {
            ExpireSession();

            throw new SessionExpiredException();
        }

        if (response.StatusCode ==
            HttpStatusCode.ServiceUnavailable)
        {
            throw new AccountDeletionException(
                "BillWatch could not safely revoke one of your bank connections, so your account was not deleted. Try again shortly.");
        }

        throw new AccountDeletionException(
            "BillWatch could not permanently delete your account right now. Your account remains available; try again.");
    }

    public void Logout()
    {
        ExpireSession();
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

public sealed class AccountRegistrationException :
    Exception
{
    public AccountRegistrationException(
        string message)
        : base(
            message)
    {
    }
}

public sealed class AccountDeletionException :
    Exception
{
    public AccountDeletionException(
        string message)
        : base(
            message)
    {
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