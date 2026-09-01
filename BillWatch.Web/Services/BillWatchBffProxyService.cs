using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BillWatch.Web.Services;

public sealed class BillWatchBffProxyService
{
    private static readonly TimeSpan
        RefreshBuffer =
            TimeSpan.FromMinutes(1);

    private readonly IHttpClientFactory
        _httpClientFactory;

    public BillWatchBffProxyService(
        IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory =
            httpClientFactory;
    }

    public Task<IResult>
        ForwardGetAsync(
            HttpContext httpContext,
            string requestUri,
            CancellationToken cancellationToken = default)
    {
        return ForwardAsync(
            httpContext,
            HttpMethod.Get,
            requestUri,
            includeEmptyJsonBody:
                false,
            cancellationToken);
    }

    public Task<IResult>
        ForwardPostAsync(
            HttpContext httpContext,
            string requestUri,
            bool includeEmptyJsonBody,
            CancellationToken cancellationToken = default)
    {
        return ForwardAsync(
            httpContext,
            HttpMethod.Post,
            requestUri,
            includeEmptyJsonBody,
            cancellationToken);
    }

    public Task<IResult>
        ForwardDeleteAsync(
            HttpContext httpContext,
            string requestUri,
            CancellationToken cancellationToken = default)
    {
        return ForwardAsync(
            httpContext,
            HttpMethod.Delete,
            requestUri,
            includeEmptyJsonBody:
                false,
            cancellationToken);
    }

    public async Task<IResult>
        ForwardDownloadAsync(
            HttpContext httpContext,
            string requestUri,
            string downloadFileName,
            string contentType,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            httpContext);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            downloadFileName);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            contentType);

        if (!IsAllowedApiPath(
                requestUri))
        {
            return Results.BadRequest();
        }

        var response =
            await SendWithRefreshAsync(
                httpContext,
                HttpMethod.Get,
                requestUri,
                includeEmptyJsonBody:
                    false,
                cancellationToken);

        if (response is null)
        {
            return Results.Unauthorized();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return await ToResultAsync(
                    response,
                    cancellationToken);
            }

            var bytes =
                await response.Content
                    .ReadAsByteArrayAsync(
                        cancellationToken);

            return Results.File(
                bytes,
                contentType,
                Path.GetFileName(
                    downloadFileName));
        }
    }

    public async Task<IResult>
        ForwardApiDownloadAsync(
            HttpContext httpContext,
            string requestUri,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            httpContext);

        if (!IsAllowedApiPath(
                requestUri))
        {
            return Results.BadRequest();
        }

        var response =
            await SendWithRefreshAsync(
                httpContext,
                HttpMethod.Get,
                requestUri,
                includeEmptyJsonBody:
                    false,
                cancellationToken);

        if (response is null)
        {
            return Results.Unauthorized();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return await ToResultAsync(
                    response,
                    cancellationToken);
            }

            var bytes =
                await response.Content
                    .ReadAsByteArrayAsync(
                        cancellationToken);

            var contentType =
                response.Content.Headers
                    .ContentType?
                    .ToString()
                ?? "application/octet-stream";

            var candidateFileName =
                response.Content.Headers
                    .ContentDisposition?
                    .FileNameStar
                ?? response.Content.Headers
                    .ContentDisposition?
                    .FileName;

            var safeFileName =
                GetSafeDownloadFileName(
                    candidateFileName);

            return Results.File(
                bytes,
                contentType,
                safeFileName);
        }
    }

    public async Task<IResult>
        DeleteAccountAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            httpContext);

        var response =
            await SendWithRefreshAsync(
                httpContext,
                HttpMethod.Delete,
                "/api/account",
                includeEmptyJsonBody:
                    false,
                cancellationToken);

        if (response is null)
        {
            return Results.Unauthorized();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return await ToResultAsync(
                    response,
                    cancellationToken);
            }

            await httpContext.SignOutAsync(
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

            return Results.NoContent();
        }
    }

    public async Task<IResult>
        ForwardMultipartFileAsync(
            HttpContext httpContext,
            string requestUri,
            IFormFile file,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            httpContext);

        ArgumentNullException.ThrowIfNull(
            file);

        if (!IsAllowedApiPath(
                requestUri))
        {
            return Results.BadRequest();
        }

        var session =
            await GetValidSessionAsync(
                httpContext,
                cancellationToken);

        if (session is null)
        {
            return Results.Unauthorized();
        }

        var response =
            await SendAuthorizedMultipartAsync(
                requestUri,
                session.AccessToken,
                file,
                cancellationToken);

        if (response.StatusCode ==
            HttpStatusCode.Unauthorized)
        {
            response.Dispose();

            session =
                await TryRefreshAsync(
                    httpContext,
                    session,
                    cancellationToken);

            if (session is null)
            {
                return Results.Unauthorized();
            }

            response =
                await SendAuthorizedMultipartAsync(
                    requestUri,
                    session.AccessToken,
                    file,
                    cancellationToken);
        }

        using (response)
        {
            return await ToResultAsync(
                response,
                cancellationToken);
        }
    }

    private async Task<IResult>
        ForwardAsync(
            HttpContext httpContext,
            HttpMethod method,
            string requestUri,
            bool includeEmptyJsonBody,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            httpContext);

        ArgumentNullException.ThrowIfNull(
            method);

        if (!IsAllowedApiPath(
                requestUri))
        {
            return Results.BadRequest();
        }

        var response =
            await SendWithRefreshAsync(
                httpContext,
                method,
                requestUri,
                includeEmptyJsonBody,
                cancellationToken);

        if (response is null)
        {
            return Results.Unauthorized();
        }

        using (response)
        {
            return await ToResultAsync(
                response,
                cancellationToken);
        }
    }

    private async Task<HttpResponseMessage?>
        SendWithRefreshAsync(
            HttpContext httpContext,
            HttpMethod method,
            string requestUri,
            bool includeEmptyJsonBody,
            CancellationToken cancellationToken)
    {
        var session =
            await GetValidSessionAsync(
                httpContext,
                cancellationToken);

        if (session is null)
        {
            return null;
        }

        var response =
            await SendAuthorizedAsync(
                method,
                requestUri,
                session.AccessToken,
                includeEmptyJsonBody,
                cancellationToken);

        if (response.StatusCode !=
            HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();

        session =
            await TryRefreshAsync(
                httpContext,
                session,
                cancellationToken);

        if (session is null)
        {
            return null;
        }

        return await SendAuthorizedAsync(
            method,
            requestUri,
            session.AccessToken,
            includeEmptyJsonBody,
            cancellationToken);
    }

    private async Task<WebApiSession?>
        GetValidSessionAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var session =
            await GetSessionAsync(
                httpContext);

        if (session is null)
        {
            return null;
        }

        if (!ShouldRefresh(
                session.ExpiresAtUtc))
        {
            return session;
        }

        return await TryRefreshAsync(
            httpContext,
            session,
            cancellationToken);
    }

    private async Task<HttpResponseMessage>
        SendAuthorizedAsync(
            HttpMethod method,
            string requestUri,
            string accessToken,
            bool includeEmptyJsonBody,
            CancellationToken cancellationToken)
    {
        var client =
            _httpClientFactory
                .CreateClient(
                    "BillWatchApi");

        using var request =
            new HttpRequestMessage(
                method,
                requestUri);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        if (includeEmptyJsonBody)
        {
            request.Content =
                JsonContent.Create(
                    new { });
        }

        return await client.SendAsync(
            request,
            HttpCompletionOption
                .ResponseHeadersRead,
            cancellationToken);
    }

    private async Task<HttpResponseMessage>
        SendAuthorizedMultipartAsync(
            string requestUri,
            string accessToken,
            IFormFile file,
            CancellationToken cancellationToken)
    {
        var client =
            _httpClientFactory
                .CreateClient(
                    "BillWatchApi");

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                requestUri);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        await using var fileStream =
            file.OpenReadStream();

        using var multipart =
            new MultipartFormDataContent();

        var fileContent =
            new StreamContent(
                fileStream);

        if (MediaTypeHeaderValue.TryParse(
                file.ContentType,
                out var contentType))
        {
            fileContent.Headers.ContentType =
                contentType;
        }

        var safeFileName =
            Path.GetFileName(
                    file.FileName)
                .Replace(
                    "\r",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    "\n",
                    string.Empty,
                    StringComparison.Ordinal);

        if (string.IsNullOrWhiteSpace(
                safeFileName))
        {
            safeFileName =
                "statement";
        }

        multipart.Add(
            fileContent,
            "file",
            safeFileName);

        request.Content =
            multipart;

        return await client.SendAsync(
            request,
            HttpCompletionOption
                .ResponseHeadersRead,
            cancellationToken);
    }

    private async Task<WebApiSession?>
        GetSessionAsync(
            HttpContext httpContext)
    {
        var authenticateResult =
            await httpContext
                .AuthenticateAsync(
                    CookieAuthenticationDefaults
                        .AuthenticationScheme);

        if (!authenticateResult.Succeeded ||
            authenticateResult.Principal is null ||
            authenticateResult.Properties is null)
        {
            return null;
        }

        var accessToken =
            authenticateResult.Properties
                .GetTokenValue(
                    "access_token");

        var refreshToken =
            authenticateResult.Properties
                .GetTokenValue(
                    "refresh_token");

        var expiresAtText =
            authenticateResult.Properties
                .GetTokenValue(
                    "expires_at");

        if (string.IsNullOrWhiteSpace(
                accessToken) ||
            string.IsNullOrWhiteSpace(
                refreshToken))
        {
            return null;
        }

        DateTimeOffset? expiresAtUtc =
            null;

        if (DateTimeOffset.TryParse(
                expiresAtText,
                out var parsedExpiresAt))
        {
            expiresAtUtc =
                parsedExpiresAt;
        }

        return new WebApiSession(
            authenticateResult.Principal,
            authenticateResult.Properties,
            accessToken,
            refreshToken,
            expiresAtUtc);
    }

    private async Task<WebApiSession?>
        TryRefreshAsync(
            HttpContext httpContext,
            WebApiSession session,
            CancellationToken cancellationToken)
    {
        var client =
            _httpClientFactory
                .CreateClient(
                    "BillWatchApi");

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        session.RefreshToken
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
            await response.Content
                .ReadFromJsonAsync<
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

        var expiresAtUtc =
            DateTimeOffset.UtcNow
                .AddSeconds(
                    Math.Max(
                        refreshedTokens.ExpiresIn,
                        0));

        var existingTokens =
            session.Properties
                .GetTokens()
                .Where(
                    token =>
                        token.Name is not
                            "access_token" and not
                            "refresh_token" and not
                            "expires_at" and not
                            "token_type")
                .ToList();

        existingTokens.Add(
            new AuthenticationToken
            {
                Name =
                    "access_token",

                Value =
                    refreshedTokens.AccessToken
            });

        existingTokens.Add(
            new AuthenticationToken
            {
                Name =
                    "refresh_token",

                Value =
                    refreshedTokens.RefreshToken
            });

        existingTokens.Add(
            new AuthenticationToken
            {
                Name =
                    "expires_at",

                Value =
                    expiresAtUtc.ToString(
                        "O")
            });

        existingTokens.Add(
            new AuthenticationToken
            {
                Name =
                    "token_type",

                Value =
                    refreshedTokens.TokenType
            });

        session.Properties.StoreTokens(
            existingTokens);

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

    private static bool ShouldRefresh(
        DateTimeOffset? expiresAtUtc)
    {
        if (expiresAtUtc is null)
        {
            return false;
        }

        return expiresAtUtc.Value <=
            DateTimeOffset.UtcNow
                .Add(
                    RefreshBuffer);
    }

    private static bool IsAllowedApiPath(
        string requestUri)
    {
        if (string.IsNullOrWhiteSpace(
                requestUri))
        {
            return false;
        }

        if (!requestUri.StartsWith(
                "/api/",
                StringComparison.Ordinal))
        {
            return false;
        }

        if (requestUri.Contains(
                "://",
                StringComparison.Ordinal) ||
            requestUri.Contains(
                '\\',
                StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static string GetSafeDownloadFileName(
        string? candidateFileName)
    {
        if (string.IsNullOrWhiteSpace(
                candidateFileName))
        {
            return "billwatch-statement";
        }

        var unquoted =
            candidateFileName
                .Trim()
                .Trim('"');

        var safeFileName =
            Path.GetFileName(
                    unquoted)
                .Replace(
                    "\r",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    "\n",
                    string.Empty,
                    StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(
                safeFileName)
            ? "billwatch-statement"
            : safeFileName;
    }

    private static async Task<IResult>
        ToResultAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
    {
        var statusCode =
            (int)response.StatusCode;

        if (response.StatusCode ==
            HttpStatusCode.NoContent)
        {
            return Results.StatusCode(
                statusCode);
        }

        var content =
            await response.Content
                .ReadAsStringAsync(
                    cancellationToken);

        if (string.IsNullOrWhiteSpace(
                content))
        {
            return Results.StatusCode(
                statusCode);
        }

        var contentType =
            response.Content.Headers
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
        System.Security.Claims
            .ClaimsPrincipal Principal,
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