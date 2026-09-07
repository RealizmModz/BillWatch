using System.Net;
using System.Security.Claims;
using System.Text;
using BillWatch.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BillWatch.Tests.Security;

public sealed class WebBffRefreshSecurityTests
{
    [Fact]
    public async Task NearExpirySession_RefreshesServerSide_WithoutReturningTokens()
    {
        const string oldAccessToken = "old-access-secret";
        const string oldRefreshToken = "old-refresh-secret";
        const string newAccessToken = "new-access-secret";
        const string newRefreshToken = "new-refresh-secret";
        const string safePayload = "{\"value\":\"safe\"}";

        using var handler =
            new CapturingHandler(
                JsonResponse(
                    HttpStatusCode.OK,
                    $"{{\"tokenType\":\"Bearer\",\"accessToken\":\"{newAccessToken}\",\"expiresIn\":3600,\"refreshToken\":\"{newRefreshToken}\"}}"),
                JsonResponse(
                    HttpStatusCode.OK,
                    safePayload));

        using var factory =
            new SingleClientFactory(
                handler);

        var authentication =
            CreateAuthentication(
                oldAccessToken,
                oldRefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(10));

        var context =
            CreateHttpContext(
                authentication);

        var service =
            new BillWatchBffProxyService(
                factory);

        var result =
            await service.ForwardGetAsync(
                context,
                "/api/bill-streams");

        Assert.Collection(
            handler.Requests,
            refresh =>
            {
                Assert.Equal(
                    HttpMethod.Post,
                    refresh.Method);

                Assert.Equal(
                    "/api/auth/refresh",
                    refresh.Path);

                Assert.Null(
                    refresh.Authorization);

                Assert.Contains(
                    oldRefreshToken,
                    refresh.Body,
                    StringComparison.Ordinal);
            },
            forwarded =>
            {
                Assert.Equal(
                    HttpMethod.Get,
                    forwarded.Method);

                Assert.Equal(
                    "/api/bill-streams",
                    forwarded.Path);

                Assert.Equal(
                    $"Bearer {newAccessToken}",
                    forwarded.Authorization);
            });

        Assert.False(
            authentication.SignedOut);

        Assert.NotNull(
            authentication.LastSignInProperties);

        Assert.Equal(
            newAccessToken,
            authentication.LastSignInProperties!
                .GetTokenValue(
                    "access_token"));

        Assert.Equal(
            newRefreshToken,
            authentication.LastSignInProperties
                .GetTokenValue(
                    "refresh_token"));

        var body =
            await ExecuteResultAsync(
                context,
                result);

        Assert.Equal(
            StatusCodes.Status200OK,
            context.Response.StatusCode);

        Assert.Equal(
            safePayload,
            body);

        Assert.DoesNotContain(
            oldAccessToken,
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            oldRefreshToken,
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            newAccessToken,
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            newRefreshToken,
            body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnauthorizedApiResponse_RefreshesAndRetriesExactlyOnce()
    {
        const string oldAccessToken = "old-access-secret";
        const string oldRefreshToken = "old-refresh-secret";
        const string newAccessToken = "new-access-secret";
        const string newRefreshToken = "new-refresh-secret";

        using var handler =
            new CapturingHandler(
                new HttpResponseMessage(
                    HttpStatusCode.Unauthorized),
                JsonResponse(
                    HttpStatusCode.OK,
                    $"{{\"tokenType\":\"Bearer\",\"accessToken\":\"{newAccessToken}\",\"expiresIn\":3600,\"refreshToken\":\"{newRefreshToken}\"}}"),
                JsonResponse(
                    HttpStatusCode.OK,
                    "{\"value\":\"retried\"}"));

        using var factory =
            new SingleClientFactory(
                handler);

        var authentication =
            CreateAuthentication(
                oldAccessToken,
                oldRefreshToken,
                DateTimeOffset.UtcNow.AddMinutes(10));

        var context =
            CreateHttpContext(
                authentication);

        var service =
            new BillWatchBffProxyService(
                factory);

        var result =
            await service.ForwardGetAsync(
                context,
                "/api/alerts");

        Assert.Collection(
            handler.Requests,
            first =>
            {
                Assert.Equal(
                    "/api/alerts",
                    first.Path);

                Assert.Equal(
                    $"Bearer {oldAccessToken}",
                    first.Authorization);
            },
            refresh =>
            {
                Assert.Equal(
                    "/api/auth/refresh",
                    refresh.Path);

                Assert.Null(
                    refresh.Authorization);

                Assert.Contains(
                    oldRefreshToken,
                    refresh.Body,
                    StringComparison.Ordinal);
            },
            retry =>
            {
                Assert.Equal(
                    "/api/alerts",
                    retry.Path);

                Assert.Equal(
                    $"Bearer {newAccessToken}",
                    retry.Authorization);
            });

        var body =
            await ExecuteResultAsync(
                context,
                result);

        Assert.Equal(
            StatusCodes.Status200OK,
            context.Response.StatusCode);

        Assert.Equal(
            "{\"value\":\"retried\"}",
            body);

        Assert.DoesNotContain(
            newAccessToken,
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            newRefreshToken,
            body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshFailure_SignsOutAndFailsClosed_WithoutForwardingRequest()
    {
        const string oldAccessToken = "old-access-secret";
        const string oldRefreshToken = "old-refresh-secret";

        using var handler =
            new CapturingHandler(
                new HttpResponseMessage(
                    HttpStatusCode.Unauthorized));

        using var factory =
            new SingleClientFactory(
                handler);

        var authentication =
            CreateAuthentication(
                oldAccessToken,
                oldRefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(10));

        var context =
            CreateHttpContext(
                authentication);

        var service =
            new BillWatchBffProxyService(
                factory);

        var result =
            await service.ForwardGetAsync(
                context,
                "/api/account/export");

        var refresh =
            Assert.Single(
                handler.Requests);

        Assert.Equal(
            "/api/auth/refresh",
            refresh.Path);

        Assert.Contains(
            oldRefreshToken,
            refresh.Body,
            StringComparison.Ordinal);

        Assert.True(
            authentication.SignedOut);

        Assert.Null(
            authentication.LastSignInProperties);

        var body =
            await ExecuteResultAsync(
                context,
                result);

        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            context.Response.StatusCode);

        Assert.DoesNotContain(
            oldAccessToken,
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            oldRefreshToken,
            body,
            StringComparison.Ordinal);
    }

    private static CapturingAuthenticationService
        CreateAuthentication(
            string accessToken,
            string refreshToken,
            DateTimeOffset expiresAtUtc)
    {
        var identity =
            new ClaimsIdentity(
                [
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        "refresh-security-test"),

                    new Claim(
                        ClaimTypes.Name,
                        "refresh-security-test@billwatch.invalid")
                ],
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

        var principal =
            new ClaimsPrincipal(
                identity);

        var properties =
            new AuthenticationProperties();

        properties.StoreTokens(
            [
                new AuthenticationToken
                {
                    Name = "access_token",
                    Value = accessToken
                },
                new AuthenticationToken
                {
                    Name = "refresh_token",
                    Value = refreshToken
                },
                new AuthenticationToken
                {
                    Name = "expires_at",
                    Value = expiresAtUtc.ToString("O")
                },
                new AuthenticationToken
                {
                    Name = "token_type",
                    Value = "Bearer"
                }
            ]);

        var ticket =
            new AuthenticationTicket(
                principal,
                properties,
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

        return new CapturingAuthenticationService(
            AuthenticateResult.Success(
                ticket));
    }

    private static DefaultHttpContext
        CreateHttpContext(
            IAuthenticationService authentication)
    {
        var services =
            new ServiceCollection()
                .AddLogging()
                .AddSingleton(
                    authentication)
                .BuildServiceProvider();

        var context =
            new DefaultHttpContext
            {
                RequestServices = services
            };

        context.Response.Body =
            new MemoryStream();

        return context;
    }

    private static async Task<string>
        ExecuteResultAsync(
            DefaultHttpContext context,
            IResult result)
    {
        await result.ExecuteAsync(
            context);

        context.Response.Body.Position =
            0;

        using var reader =
            new StreamReader(
                context.Response.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);

        return await reader.ReadToEndAsync();
    }

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string json)
    {
        return new HttpResponseMessage(
            statusCode)
        {
            Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
        };
    }

    private sealed class SingleClientFactory
        : IHttpClientFactory,
          IDisposable
    {
        private readonly HttpClient _client;

        public SingleClientFactory(
            HttpMessageHandler handler)
        {
            _client =
                new HttpClient(
                    handler,
                    disposeHandler: false)
                {
                    BaseAddress =
                        new Uri(
                            "https://api.invalid")
                };
        }

        public HttpClient CreateClient(
            string name)
        {
            Assert.Equal(
                "BillWatchApi",
                name);

            return _client;
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }

    private sealed class CapturingHandler(
        params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage>
            _responses =
                new(
                    responses);

        public List<CapturedRequest> Requests { get; } =
            [];

        protected override async Task<HttpResponseMessage>
            SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    "No scripted HTTP response remains.");
            }

            var body =
                request.Content is null
                    ? string.Empty
                    : await request.Content
                        .ReadAsStringAsync(
                            cancellationToken);

            Requests.Add(
                new CapturedRequest(
                    request.Method,
                    request.RequestUri?.AbsolutePath
                    ?? throw new InvalidOperationException(
                        "Request URI was missing."),
                    request.Headers.Authorization?
                        .ToString(),
                    body));

            return _responses.Dequeue();
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Path,
        string? Authorization,
        string Body);

    private sealed class CapturingAuthenticationService(
        AuthenticateResult authenticateResult)
        : IAuthenticationService
    {
        public bool SignedOut { get; private set; }

        public AuthenticationProperties?
            LastSignInProperties { get; private set; }

        public Task<AuthenticateResult>
            AuthenticateAsync(
                HttpContext context,
                string? scheme)
        {
            Assert.Equal(
                CookieAuthenticationDefaults
                    .AuthenticationScheme,
                scheme);

            return Task.FromResult(
                authenticateResult);
        }

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            throw new NotSupportedException();
        }

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            throw new NotSupportedException();
        }

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties)
        {
            Assert.Equal(
                CookieAuthenticationDefaults
                    .AuthenticationScheme,
                scheme);

            LastSignInProperties =
                properties;

            return Task.CompletedTask;
        }

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            Assert.Equal(
                CookieAuthenticationDefaults
                    .AuthenticationScheme,
                scheme);

            SignedOut =
                true;

            return Task.CompletedTask;
        }
    }
}
