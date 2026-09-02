using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BillWatch.Web.Services;

public sealed class AdminBffWriteProxyService(
    IHttpClientFactory httpClientFactory)
{
    private static readonly TimeSpan RefreshBuffer =
        TimeSpan.FromMinutes(1);

    public Task<IResult> ForwardJsonAsync<T>(
        HttpContext httpContext,
        HttpMethod method,
        string requestUri,
        T body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(method);

        if (method != HttpMethod.Post &&
            method != HttpMethod.Put)
        {
            return Task.FromResult<IResult>(
                Results.BadRequest());
        }

        if (!IsAllowedApiPath(requestUri))
        {
            return Task.FromResult<IResult>(
                Results.BadRequest());
        }

        return ForwardCoreAsync(
            httpContext,
            method,
            requestUri,
            body,
            cancellationToken);
    }

    private async Task<IResult> ForwardCoreAsync<T>(
        HttpContext httpContext,
        HttpMethod method,
        string requestUri,
        T body,
        CancellationToken cancellationToken)
    {
        var session = await GetValidSessionAsync(
            httpContext,
            cancellationToken);

        if (session is null)
        {
            return Results.Unauthorized();
        }

        var response = await SendAuthorizedJsonAsync(
            method,
            requestUri,
            session.AccessToken,
            body,
            cancellationToken);

        if (response.StatusCode ==
            HttpStatusCode.Unauthorized)
        {
            response.Dispose();

            session = await TryRefreshAsync(
                httpContext,
                session,
                cancellationToken);

            if (session is null)
            {
                return Results.Unauthorized();
            }

            response = await SendAuthorizedJsonAsync(
                method,
                requestUri,
                session.AccessToken,
                body,
                cancellationToken);
        }

        using (response)
        {
            return await ToResultAsync(
                response,
                cancellationToken);
        }
    }

    private async Task<HttpResponseMessage>
        SendAuthorizedJsonAsync<T>(
            HttpMethod method,
            string requestUri,
            string accessToken,
            T body,
            CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(
            "BillWatchApi");

        using var request = new HttpRequestMessage(
            method,
            requestUri);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        request.Content = JsonContent.Create(body);

        return await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private async Task<WebApiSession?> GetValidSessionAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(httpContext);

        if (session is null)
        {
            return null;
        }

        if (session.ExpiresAtUtc is null ||
            session.ExpiresAtUtc.Value >
                DateTimeOffset.UtcNow.Add(RefreshBuffer))
        {
            return session;
        }

        return await TryRefreshAsync(
            httpContext,
            session,
            cancellationToken);
    }

    private static async Task<WebApiSession?> GetSessionAsync(
        HttpContext httpContext)
    {
        var authenticateResult =
            await httpContext.AuthenticateAsync(
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

        if (!authenticateResult.Succeeded ||
            authenticateResult.Principal is null ||
            authenticateResult.Properties is null)
        {
            return null;
        }

        var accessToken =
            authenticateResult.Properties.GetTokenValue(
                "access_token");

        var refreshToken =
            authenticateResult.Properties.GetTokenValue(
                "refresh_token");

        var expiresAtText =
            authenticateResult.Properties.GetTokenValue(
                "expires_at");

        if (string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        DateTimeOffset? expiresAtUtc = null;

        if (DateTimeOffset.TryParse(
                expiresAtText,
                out var parsedExpiresAt))
        {
            expiresAtUtc = parsedExpiresAt;
        }

        return new WebApiSession(
            authenticateResult.Principal,
            authenticateResult.Properties,
            accessToken,
            refreshToken,
            expiresAtUtc);
    }

    private async Task<WebApiSession?> TryRefreshAsync(
        HttpContext httpContext,
        WebApiSession session,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(
            "BillWatchApi");

        using var response = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new
            {
                refreshToken = session.RefreshToken
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            await httpContext.SignOutAsync(
                CookieAuthenticationDefaults
                    .AuthenticationScheme);
            return null;
        }

        var refreshedTokens =
            await response.Content.ReadFromJsonAsync<
                RefreshedTokenResponse>(
                cancellationToken:
                    cancellationToken);

        if (refreshedTokens is null ||
            string.IsNullOrWhiteSpace(
                refreshedTokens.AccessToken) ||
            string.IsNullOrWhiteSpace(
                refreshedTokens.RefreshToken))
        {
            await httpContext.SignOutAsync(
                CookieAuthenticationDefaults
                    .AuthenticationScheme);
            return null;
        }

        var expiresAtUtc = DateTimeOffset.UtcNow
            .AddSeconds(
                Math.Max(
                    refreshedTokens.ExpiresIn,
                    0));

        var existingTokens = session.Properties
            .GetTokens()
            .Where(
                token => token.Name is not
                    "access_token" and not
                    "refresh_token" and not
                    "expires_at" and not
                    "token_type")
            .ToList();

        existingTokens.Add(
            new AuthenticationToken
            {
                Name = "access_token",
                Value = refreshedTokens.AccessToken
            });

        existingTokens.Add(
            new AuthenticationToken
            {
                Name = "refresh_token",
                Value = refreshedTokens.RefreshToken
            });

        existingTokens.Add(
            new AuthenticationToken
            {
                Name = "expires_at",
                Value = expiresAtUtc.ToString("O")
            });

        existingTokens.Add(
            new AuthenticationToken
            {
                Name = "token_type",
                Value = refreshedTokens.TokenType
            });

        session.Properties.StoreTokens(existingTokens);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults
                .AuthenticationScheme,
            session.Principal,
            session.Properties);

        return new WebApiSession(
            session.Principal,
            session.Properties,
            refreshedTokens.AccessToken,
            refreshedTokens.RefreshToken,
            expiresAtUtc);
    }

    private static bool IsAllowedApiPath(
        string requestUri)
    {
        if (string.IsNullOrWhiteSpace(requestUri) ||
            !requestUri.StartsWith(
                "/api/admin/",
                StringComparison.Ordinal))
        {
            return false;
        }

        return !requestUri.Contains(
                   "://",
                   StringComparison.Ordinal) &&
               !requestUri.Contains(
                   '\\',
                   StringComparison.Ordinal);
    }

    private static async Task<IResult> ToResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;

        if (response.StatusCode ==
            HttpStatusCode.NoContent)
        {
            return Results.StatusCode(statusCode);
        }

        var content = await response.Content
            .ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
        {
            return Results.StatusCode(statusCode);
        }

        var contentType = response.Content.Headers
            .ContentType?
            .ToString()
            ?? "application/json; charset=utf-8";

        return Results.Content(
            content,
            contentType,
            Encoding.UTF8,
            statusCode);
    }

    private sealed record WebApiSession(
        System.Security.Claims.ClaimsPrincipal Principal,
        AuthenticationProperties Properties,
        string AccessToken,
        string RefreshToken,
        DateTimeOffset? ExpiresAtUtc);

    private sealed record RefreshedTokenResponse(
        string TokenType,
        string AccessToken,
        long ExpiresIn,
        string RefreshToken);
}
