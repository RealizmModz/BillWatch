using System.Security.Claims;
using BillWatch.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BillWatch.Web.Infrastructure;

public static class ExternalAuthenticationEndpointMappings
{
    public const string ExternalCookieScheme =
        "BillWatch.Web.External";

    private const string ExternalIdTokenProperty =
        "billwatch:external-id-token";

    private const string ExternalProviderProperty =
        "billwatch:external-provider";

    private static readonly ExternalProviderDefinition[] Providers =
    [
        new(
            Provider: "google",
            Scheme: "BillWatch.Web.Google",
            DisplayName: "Google",
            Authority: "https://accounts.google.com",
            CallbackPath: "/signin-billwatch-google",
            ResponseMode: OpenIdConnectResponseMode.Query,
            IncludeProfileScope: true),

        new(
            Provider: "apple",
            Scheme: "BillWatch.Web.Apple",
            DisplayName: "Apple",
            Authority: "https://appleid.apple.com",
            CallbackPath: "/signin-billwatch-apple",
            ResponseMode: OpenIdConnectResponseMode.FormPost,
            IncludeProfileScope: false),

        new(
            Provider: "microsoft",
            Scheme: "BillWatch.Web.Microsoft",
            DisplayName: "Microsoft",
            Authority: "https://login.microsoftonline.com/consumers/v2.0",
            CallbackPath: "/signin-billwatch-microsoft",
            ResponseMode: OpenIdConnectResponseMode.Query,
            IncludeProfileScope: true)
    ];

    public static AuthenticationBuilder
        AddBillWatchExternalAuthentication(
            this AuthenticationBuilder authenticationBuilder,
            IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(
            authenticationBuilder);
        ArgumentNullException.ThrowIfNull(
            configuration);

        var configuredProviders =
            configuration
                .GetSection(
                    ExternalWebIdentityOptions.SectionName)
                .Get<ExternalWebIdentityOptions>()
            ?? new ExternalWebIdentityOptions();

        authenticationBuilder.AddCookie(
            ExternalCookieScheme,
            options =>
            {
                options.Cookie.Name =
                    "__Host-BillWatch.Web.External";

                options.Cookie.HttpOnly =
                    true;

                options.Cookie.SecurePolicy =
                    CookieSecurePolicy.Always;

                options.Cookie.SameSite =
                    SameSiteMode.Lax;

                options.Cookie.Path =
                    "/";

                options.ExpireTimeSpan =
                    TimeSpan.FromMinutes(5);

                options.SlidingExpiration =
                    false;
            });

        foreach (var provider in Providers)
        {
            var credentials =
                configuredProviders.GetProvider(
                    provider.Provider);

            if (credentials is null ||
                !credentials.IsConfigured)
            {
                continue;
            }

            authenticationBuilder.AddOpenIdConnect(
                provider.Scheme,
                provider.DisplayName,
                options =>
                    ConfigureOpenIdConnect(
                        options,
                        provider,
                        credentials));
        }

        return authenticationBuilder;
    }

    public static IEndpointRouteBuilder
        MapBillWatchExternalAuthenticationEndpoints(
            this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(
            endpoints);

        endpoints.MapGet(
                "/auth/external/{provider}",
                BeginExternalSignInAsync)
            .AllowAnonymous();

        endpoints.MapGet(
                "/auth/external/complete",
                CompleteExternalSignInAsync)
            .AllowAnonymous();

        return endpoints;
    }

    private static async Task<IResult>
        BeginExternalSignInAsync(
            string provider,
            IAuthenticationSchemeProvider schemeProvider)
    {
        var providerDefinition =
            FindProvider(
                provider);

        if (providerDefinition is null)
        {
            return Results.NotFound();
        }

        var registeredScheme =
            await schemeProvider.GetSchemeAsync(
                providerDefinition.Scheme);

        if (registeredScheme is null)
        {
            return Results.Redirect(
                BuildLoginErrorRedirect(
                    "That sign-in option is not available yet."));
        }

        var normalizedProvider =
            providerDefinition.Provider;

        var properties =
            new AuthenticationProperties
            {
                RedirectUri =
                    "/auth/external/complete?provider=" +
                    Uri.EscapeDataString(
                        normalizedProvider)
            };

        properties.Items[
            ExternalProviderProperty] =
            normalizedProvider;

        return Results.Challenge(
            properties,
            [
                providerDefinition.Scheme
            ]);
    }

    private static async Task<IResult>
        CompleteExternalSignInAsync(
            HttpContext context,
            string? provider,
            WebAuthenticationService authenticationService,
            CancellationToken cancellationToken)
    {
        var providerDefinition =
            FindProvider(
                provider);

        if (providerDefinition is null)
        {
            await ClearExternalSessionAsync(
                context);

            return Results.Redirect(
                BuildLoginErrorRedirect(
                    "External sign-in could not be completed."));
        }

        var externalResult =
            await context.AuthenticateAsync(
                ExternalCookieScheme);

        if (!externalResult.Succeeded ||
            externalResult.Principal is null ||
            externalResult.Properties is null)
        {
            await ClearExternalSessionAsync(
                context);

            return Results.Redirect(
                BuildLoginErrorRedirect(
                    "External sign-in could not be completed."));
        }

        externalResult.Properties.Items.TryGetValue(
            ExternalProviderProperty,
            out var storedProvider);

        if (!string.Equals(
                storedProvider,
                providerDefinition.Provider,
                StringComparison.Ordinal))
        {
            await ClearExternalSessionAsync(
                context);

            return Results.Redirect(
                BuildLoginErrorRedirect(
                    "External sign-in could not be completed."));
        }

        externalResult.Properties.Items.TryGetValue(
            ExternalIdTokenProperty,
            out var idToken);

        var subject =
            externalResult.Principal
                .FindFirst("sub")?
                .Value;

        var email =
            externalResult.Principal
                .FindFirst("email")?
                .Value ??
            externalResult.Principal
                .FindFirst(ClaimTypes.Email)?
                .Value;

        if (string.IsNullOrWhiteSpace(
                idToken) ||
            string.IsNullOrWhiteSpace(
                subject))
        {
            await ClearExternalSessionAsync(
                context);

            return Results.Redirect(
                BuildLoginErrorRedirect(
                    "External sign-in could not be completed."));
        }

        var loginResult =
            await authenticationService
                .LoginExternalAsync(
                    context,
                    providerDefinition.Provider,
                    idToken,
                    subject,
                    email,
                    cancellationToken);

        await ClearExternalSessionAsync(
            context);

        if (!loginResult.Succeeded)
        {
            return Results.Redirect(
                BuildLoginErrorRedirect(
                    loginResult.ErrorMessage ??
                    "External sign-in could not be completed."));
        }

        return Results.Redirect(
            "/app");
    }

    private static void ConfigureOpenIdConnect(
        OpenIdConnectOptions options,
        ExternalProviderDefinition provider,
        ExternalWebIdentityCredentialOptions credentials)
    {
        options.SignInScheme =
            ExternalCookieScheme;

        options.Authority =
            provider.Authority;

        options.ClientId =
            credentials.ClientId!.Trim();

        options.ClientSecret =
            credentials.ClientSecret!.Trim();

        options.CallbackPath =
            provider.CallbackPath;

        options.ResponseType =
            OpenIdConnectResponseType.Code;

        options.ResponseMode =
            provider.ResponseMode;

        options.UsePkce =
            true;

        options.RequireHttpsMetadata =
            true;

        options.GetClaimsFromUserInfoEndpoint =
            false;

        /*
         * BillWatch needs only the provider-issued ID token long enough to
         * validate the linked identity again at the API boundary. Provider
         * access and refresh tokens are deliberately not persisted.
         */
        options.SaveTokens =
            false;

        options.MapInboundClaims =
            false;

        options.Scope.Clear();
        options.Scope.Add(
            OpenIdConnectScope.OpenId);
        options.Scope.Add(
            OpenIdConnectScope.Email);

        if (provider.IncludeProfileScope)
        {
            options.Scope.Add(
                OpenIdConnectScope.Profile);
        }

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer =
                    true,

                ValidateAudience =
                    true,

                NameClaimType =
                    "email"
            };

        ConfigureRemoteCookie(
            options.CorrelationCookie,
            $"__Host-BillWatch.Web.{provider.DisplayName}.Correlation.");

        ConfigureRemoteCookie(
            options.NonceCookie,
            $"__Host-BillWatch.Web.{provider.DisplayName}.Nonce.");

        options.Events =
            new OpenIdConnectEvents
            {
                OnTokenValidated =
                    context =>
                    {
                        var idToken =
                            context.TokenEndpointResponse?
                                .IdToken;

                        var properties =
                            context.Properties;

                        if (string.IsNullOrWhiteSpace(
                                idToken) ||
                            properties is null)
                        {
                            context.Fail(
                                "The identity provider did not return a valid sign-in state.");

                            return Task.CompletedTask;
                        }

                        properties.Items[
                            ExternalIdTokenProperty] =
                            idToken;

                        properties.Items[
                            ExternalProviderProperty] =
                            provider.Provider;

                        return Task.CompletedTask;
                    },

                OnRemoteFailure =
                    context =>
                    {
                        context.HandleResponse();

                        context.Response.Redirect(
                            BuildLoginErrorRedirect(
                                "External sign-in could not be completed."));

                        return Task.CompletedTask;
                    }
            };
    }

    private static void ConfigureRemoteCookie(
        CookieBuilder cookie,
        string name)
    {
        cookie.Name =
            name;

        cookie.HttpOnly =
            true;

        cookie.SecurePolicy =
            CookieSecurePolicy.Always;

        cookie.SameSite =
            SameSiteMode.None;

        cookie.Path =
            "/";

        cookie.IsEssential =
            true;
    }

    private static ExternalProviderDefinition?
        FindProvider(
            string? provider)
    {
        if (string.IsNullOrWhiteSpace(
                provider))
        {
            return null;
        }

        var normalizedProvider =
            provider.Trim()
                .ToLowerInvariant();

        return Providers.FirstOrDefault(
            candidate =>
                string.Equals(
                    candidate.Provider,
                    normalizedProvider,
                    StringComparison.Ordinal));
    }

    private static async Task ClearExternalSessionAsync(
        HttpContext context)
    {
        await context.SignOutAsync(
            ExternalCookieScheme);
    }

    private static string BuildLoginErrorRedirect(
        string error)
    {
        return "/login?error=" +
               Uri.EscapeDataString(
                   error);
    }

    private sealed record ExternalProviderDefinition(
        string Provider,
        string Scheme,
        string DisplayName,
        string Authority,
        string CallbackPath,
        string ResponseMode,
        bool IncludeProfileScope);
}

public sealed class ExternalWebIdentityOptions
{
    public const string SectionName =
        "ExternalIdentity";

    public ExternalWebIdentityCredentialOptions Google { get; set; } =
        new();

    public ExternalWebIdentityCredentialOptions Apple { get; set; } =
        new();

    public ExternalWebIdentityCredentialOptions Microsoft { get; set; } =
        new();

    public ExternalWebIdentityCredentialOptions? GetProvider(
        string provider)
    {
        return provider switch
        {
            "google" =>
                Google,

            "apple" =>
                Apple,

            "microsoft" =>
                Microsoft,

            _ =>
                null
        };
    }
}

public sealed class ExternalWebIdentityCredentialOptions
{
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(
            ClientId) &&
        !string.IsNullOrWhiteSpace(
            ClientSecret);
}
