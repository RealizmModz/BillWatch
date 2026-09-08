using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BillWatch.Web.Services;

public static class ExternalWebSignInFlow
{
    public static async Task<AuthOperationResult> LoginAsync(
        HttpContext httpContext,
        IHttpClientFactory httpClientFactory,
        string provider,
        string idToken,
        string providerSubject,
        string? email,
        string? twoFactorCode,
        string? recoveryCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        provider = provider.Trim().ToLowerInvariant();
        providerSubject = providerSubject.Trim();
        email = string.IsNullOrWhiteSpace(email)
            ? null
            : email.Trim();
        twoFactorCode = NormalizeOptionalCode(twoFactorCode);
        recoveryCode = NormalizeOptionalCode(recoveryCode);

        if (string.IsNullOrWhiteSpace(provider) ||
            string.IsNullOrWhiteSpace(idToken) ||
            string.IsNullOrWhiteSpace(providerSubject))
        {
            return new AuthOperationResult(
                false,
                "External sign-in could not be completed.");
        }

        var client = httpClientFactory.CreateClient("BillWatchApi");

        using var response = await client.PostAsJsonAsync(
            "/api/auth/external/login",
            new
            {
                provider,
                idToken,
                twoFactorCode,
                twoFactorRecoveryCode = recoveryCode
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized &&
                string.IsNullOrWhiteSpace(twoFactorCode) &&
                string.IsNullOrWhiteSpace(recoveryCode) &&
                await IsTwoFactorRequiredAsync(response, cancellationToken))
            {
                return AuthOperationResult.TwoFactorRequired;
            }

            return new AuthOperationResult(
                false,
                response.StatusCode == HttpStatusCode.TooManyRequests
                    ? "Too many sign-in attempts. Wait a minute and try again."
                    : "External sign-in could not be completed. Sign in with email and password or try again.");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<AccessTokenResponse>(
            cancellationToken: cancellationToken);

        if (tokenResponse is null ||
            string.IsNullOrWhiteSpace(tokenResponse.AccessToken) ||
            string.IsNullOrWhiteSpace(tokenResponse.RefreshToken) ||
            tokenResponse.ExpiresIn <= 0)
        {
            return new AuthOperationResult(
                false,
                "BillWatch received an invalid external sign-in response.");
        }

        var claims = new List<Claim>
        {
            new(
                ClaimTypes.Name,
                email ?? "BillWatch user"),
            new(
                ClaimTypes.NameIdentifier,
                $"{provider}:{providerSubject}")
        };

        if (!string.IsNullOrWhiteSpace(email))
        {
            claims.Add(
                new Claim(
                    ClaimTypes.Email,
                    email));
        }

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = new ClaimsPrincipal(identity);
        var now = DateTimeOffset.UtcNow;
        var accessTokenExpiresAt = now.AddSeconds(tokenResponse.ExpiresIn);

        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = true,
            ExpiresUtc = now.AddHours(12)
        };

        properties.StoreTokens(
        [
            new AuthenticationToken
            {
                Name = "access_token",
                Value = tokenResponse.AccessToken
            },
            new AuthenticationToken
            {
                Name = "refresh_token",
                Value = tokenResponse.RefreshToken
            },
            new AuthenticationToken
            {
                Name = "expires_at",
                Value = accessTokenExpiresAt.ToString("O")
            },
            new AuthenticationToken
            {
                Name = "token_type",
                Value = tokenResponse.TokenType
            }
        ]);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties);

        return AuthOperationResult.Success;
    }

    private static async Task<bool> IsTwoFactorRequiredAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty(
                    "detail",
                    out var detail))
            {
                return false;
            }

            return string.Equals(
                detail.GetString(),
                "RequiresTwoFactor",
                StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? NormalizeOptionalCode(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
