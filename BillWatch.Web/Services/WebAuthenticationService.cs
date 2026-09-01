using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BillWatch.Web.Services;

public sealed class WebAuthenticationService
{
    private readonly IHttpClientFactory
        _httpClientFactory;

    public WebAuthenticationService(
        IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory =
            httpClientFactory;
    }

    public async Task<AuthOperationResult>
        LoginAsync(
            HttpContext httpContext,
            string email,
            string password,
            bool rememberMe,
            CancellationToken cancellationToken = default)
    {
        email =
            email.Trim();

        var client =
            _httpClientFactory
                .CreateClient(
                    "BillWatchApi");

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email,
                    password
                },
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new AuthOperationResult(
                false,
                GetSafeLoginError(
                    response.StatusCode));
        }

        var tokenResponse =
            await response.Content
                .ReadFromJsonAsync<
                    AccessTokenResponse>(
                    cancellationToken:
                        cancellationToken);

        if (tokenResponse is null ||
            string.IsNullOrWhiteSpace(
                tokenResponse.AccessToken) ||
            string.IsNullOrWhiteSpace(
                tokenResponse.RefreshToken))
        {
            return new AuthOperationResult(
                false,
                "BillWatch received an invalid sign-in response.");
        }

        await SignInWebSessionAsync(
            httpContext,
            email,
            tokenResponse,
            rememberMe);

        return AuthOperationResult.Success;
    }

    public async Task<AuthOperationResult>
        RegisterAsync(
            HttpContext httpContext,
            string email,
            string password,
            CancellationToken cancellationToken = default)
    {
        email =
            email.Trim();

        var client =
            _httpClientFactory
                .CreateClient(
                    "BillWatchApi");

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    email,
                    password
                },
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error =
                await ReadRegistrationErrorAsync(
                    response,
                    cancellationToken);

            return new AuthOperationResult(
                false,
                error);
        }

        return await LoginAsync(
            httpContext,
            email,
            password,
            rememberMe: false,
            cancellationToken);
    }

    public async Task LogoutAsync(
        HttpContext httpContext)
    {
        await httpContext.SignOutAsync(
            CookieAuthenticationDefaults
                .AuthenticationScheme);
    }

    private static async Task
        SignInWebSessionAsync(
            HttpContext httpContext,
            string email,
            AccessTokenResponse tokenResponse,
            bool rememberMe)
    {
        var claims =
            new List<Claim>
            {
                new(
                    ClaimTypes.Name,
                    email),

                new(
                    ClaimTypes.Email,
                    email),

                new(
                    ClaimTypes.NameIdentifier,
                    email)
            };

        var identity =
            new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

        var principal =
            new ClaimsPrincipal(
                identity);

        var now =
            DateTimeOffset.UtcNow;

        var accessTokenExpiresAt =
            now.AddSeconds(
                tokenResponse.ExpiresIn);

        var properties =
            new AuthenticationProperties
            {
                IsPersistent =
                    rememberMe,

                AllowRefresh =
                    true,

                ExpiresUtc =
                    rememberMe
                        ? now.AddDays(30)
                        : now.AddHours(12)
            };

        properties.StoreTokens(
            [
                new AuthenticationToken
                {
                    Name =
                        "access_token",

                    Value =
                        tokenResponse.AccessToken
                },

                new AuthenticationToken
                {
                    Name =
                        "refresh_token",

                    Value =
                        tokenResponse.RefreshToken
                },

                new AuthenticationToken
                {
                    Name =
                        "expires_at",

                    Value =
                        accessTokenExpiresAt
                            .ToString("O")
                },

                new AuthenticationToken
                {
                    Name =
                        "token_type",

                    Value =
                        tokenResponse.TokenType
                }
            ]);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults
                .AuthenticationScheme,
            principal,
            properties);
    }

    private static string
        GetSafeLoginError(
            HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest =>
                "Email or password is incorrect.",

            HttpStatusCode.Unauthorized =>
                "Email or password is incorrect.",

            HttpStatusCode.Forbidden =>
                "Email or password is incorrect.",

            HttpStatusCode.TooManyRequests =>
                "Too many sign-in attempts. Wait a minute and try again.",

            _ =>
                "BillWatch could not sign you in right now."
        };
    }

    private static async Task<string>
        ReadRegistrationErrorAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
    {
        if (response.StatusCode ==
            HttpStatusCode.TooManyRequests)
        {
            return
                "Too many attempts. Wait a minute and try again.";
        }

        var body =
            await response.Content
                .ReadAsStringAsync(
                    cancellationToken);

        if (string.IsNullOrWhiteSpace(
                body))
        {
            return
                "BillWatch could not create the account.";
        }

        try
        {
            using var document =
                JsonDocument.Parse(
                    body);

            var root =
                document.RootElement;

            if (root.TryGetProperty(
                    "errors",
                    out var errors) &&
                errors.ValueKind ==
                    JsonValueKind.Object)
            {
                foreach (var property
                    in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind !=
                        JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var item
                        in property.Value
                            .EnumerateArray())
                    {
                        var message =
                            item.GetString();

                        if (!string.IsNullOrWhiteSpace(
                                message))
                        {
                            return message;
                        }
                    }
                }
            }

            if (root.TryGetProperty(
                    "detail",
                    out var detail))
            {
                var message =
                    detail.GetString();

                if (!string.IsNullOrWhiteSpace(
                        message))
                {
                    return message;
                }
            }

            if (root.TryGetProperty(
                    "title",
                    out var title))
            {
                var message =
                    title.GetString();

                if (!string.IsNullOrWhiteSpace(
                        message))
                {
                    return message;
                }
            }
        }
        catch (JsonException)
        {
            // Do not expose an unexpected raw server response.
        }

        return
            "BillWatch could not create the account. Check the information and try again.";
    }
}

public sealed record AuthOperationResult(
    bool Succeeded,
    string? ErrorMessage)
{
    public static AuthOperationResult Success { get; } =
        new(
            true,
            null);
}

public sealed record AccessTokenResponse(
    string TokenType,
    string AccessToken,
    long ExpiresIn,
    string RefreshToken);