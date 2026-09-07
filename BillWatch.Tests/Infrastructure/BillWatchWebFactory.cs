using System.Security.Claims;
using System.Text.Encodings.Web;
using BillWatch.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BillWatch.Tests.Infrastructure;

public sealed class BillWatchWebFactory
    : WebApplicationFactory<WebAssemblyMarker>
{
    private const string TestAuthenticationScheme =
        "BillWatch.Tests";

    public HttpClient CreateHttpsClient()
    {
        return CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress =
                    new Uri(
                        "https://localhost"),

                AllowAutoRedirect =
                    false,

                HandleCookies =
                    true
            });
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment(
            "Development");

        builder.UseSetting(
            "BillWatchApi:BaseUrl",
            "https://api.invalid");

        builder.ConfigureServices(
            services =>
            {
                services.AddAuthentication(
                        options =>
                        {
                            options.DefaultAuthenticateScheme =
                                TestAuthenticationScheme;

                            options.DefaultChallengeScheme =
                                TestAuthenticationScheme;

                            options.DefaultForbidScheme =
                                TestAuthenticationScheme;
                        })
                    .AddScheme<
                        AuthenticationSchemeOptions,
                        TestAuthenticationHandler>(
                        TestAuthenticationScheme,
                        _ =>
                        {
                        });
            });
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            logger,
            encoder)
    {
        protected override Task<AuthenticateResult>
            HandleAuthenticateAsync()
        {
            var identity =
                new ClaimsIdentity(
                    [
                        new Claim(
                            ClaimTypes.NameIdentifier,
                            Guid.NewGuid().ToString("D")),

                        new Claim(
                            ClaimTypes.Name,
                            "billwatch-web-test")
                    ],
                    Scheme.Name);

            var principal =
                new ClaimsPrincipal(
                    identity);

            var ticket =
                new AuthenticationTicket(
                    principal,
                    Scheme.Name);

            return Task.FromResult(
                AuthenticateResult.Success(
                    ticket));
        }

        protected override Task HandleChallengeAsync(
            AuthenticationProperties properties)
        {
            Response.StatusCode =
                StatusCodes.Status401Unauthorized;

            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(
            AuthenticationProperties properties)
        {
            Response.StatusCode =
                StatusCodes.Status403Forbidden;

            return Task.CompletedTask;
        }
    }
}
