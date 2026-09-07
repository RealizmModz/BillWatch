using System.Globalization;
using BillWatch.Web.Components;
using BillWatch.Web.Infrastructure;
using BillWatch.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Localization.Routing;

const long StatementMultipartBodyLimit =
    16L * 1024 * 1024;

var webCulture =
    CultureInfo.GetCultureInfo(
        "en-US");

CultureInfo.DefaultThreadCurrentCulture =
    webCulture;

CultureInfo.DefaultThreadCurrentUICulture =
    webCulture;

var builder =
    WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(
    options =>
    {
        options.AddServerHeader =
            false;
    });

builder.Services.AddLocalization(
    options =>
    {
        options.ResourcesPath =
            "Resources";
    });

builder.Services.Configure<RequestLocalizationOptions>(
    options =>
    {
        var spanishCulture =
            CultureInfo.GetCultureInfo(
                "es");

        options.DefaultRequestCulture =
            new RequestCulture(
                culture: webCulture,
                uiCulture: webCulture);

        /*
         * Keep formatting culture on en-US for now.
         *
         * BillWatch still has several USD values formatted through the
         * current culture. Allowing a browser language to change
         * CurrentCulture could make a USD amount display with the wrong
         * currency symbol. UI language is therefore localized independently
         * through CurrentUICulture until all money presentation is explicitly
         * currency-code-aware.
         */
        options.SupportedCultures =
            [
                webCulture
            ];

        options.SupportedUICultures =
            [
                webCulture,
                spanishCulture
            ];

        options.ApplyCurrentCultureToResponseHeaders =
            true;

        options.RequestCultureProviders.Clear();

        options.RequestCultureProviders.Add(
            new CustomRequestCultureProvider(
                context =>
                {
                    var uiCulture =
                        ResolveUiCulture(
                            context.Request.Headers
                                .AcceptLanguage
                                .ToString());

                    return Task.FromResult<ProviderCultureResult?>(
                        new ProviderCultureResult(
                            culture: webCulture.Name,
                            uiCulture: uiCulture));
                }));
    });

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services
    .AddCascadingAuthenticationState();

builder.Services
    .AddAuthentication(
        CookieAuthenticationDefaults
            .AuthenticationScheme)
    .AddCookie(
        options =>
        {
            options.Cookie.Name =
                "__Host-BillWatch.Web.Auth";

            options.Cookie.HttpOnly =
                true;

            options.Cookie.SecurePolicy =
                CookieSecurePolicy.Always;

            options.Cookie.SameSite =
                SameSiteMode.Lax;

            options.Cookie.Path =
                "/";

            options.LoginPath =
                "/login";

            options.AccessDeniedPath =
                "/login";

            options.SlidingExpiration =
                false;
        });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();

builder.Services.AddAntiforgery(
    options =>
    {
        options.HeaderName =
            "X-CSRF-TOKEN";
    });

builder.Services.Configure<FormOptions>(
    options =>
    {
        options.MultipartBodyLengthLimit =
            StatementMultipartBodyLimit;
    });

var hostingConfiguration =
    builder.ConfigureBillWatchWebHosting();

builder.Services.AddHttpClient(
    "BillWatchApi",
    client =>
    {
        client.BaseAddress =
            hostingConfiguration.ApiBaseUri;

        if (!string.IsNullOrWhiteSpace(
                hostingConfiguration.ApiHostHeader))
        {
            client.DefaultRequestHeaders.Host =
                hostingConfiguration.ApiHostHeader;
        }

        client.Timeout =
            TimeSpan.FromSeconds(30);
    });

builder.Services.AddScoped<
    WebAuthenticationService>();

builder.Services.AddScoped<
    BillWatchBffProxyService>();

builder.Services.AddScoped<
    AdminBffWriteProxyService>();

var app =
    builder.Build();

if (hostingConfiguration.UseForwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseRequestLocalization();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseBillWatchWebSecurityHeaders();

app.UseWhen(
    context =>
        !context.Request.Path.StartsWithSegments(
            "/health"),
    branch =>
        branch.UseHttpsRedirection());

app.UseAuthentication();
app.UseAuthorization();
app.UseBillWatchAntiforgeryBoundary();
app.UseAntiforgery();

app.MapStaticAssets();

app.MapBillWatchHealthEndpoints();
app.MapBillWatchAuthEndpoints();
app.MapBillWatchBffEndpoints();
app.MapBillWatchAdminBffEndpoints();
app.MapBillWatchAccountPreferenceBffEndpoints();
app.MapBillWatchAccountSecurityBffEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string ResolveUiCulture(
    string? acceptLanguageHeader)
{
    if (string.IsNullOrWhiteSpace(
            acceptLanguageHeader))
    {
        return "en-US";
    }

    var preferences =
        acceptLanguageHeader
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(
                (entry, index) =>
                    ParseLanguagePreference(
                        entry,
                        index))
            .Where(
                preference =>
                    preference.Quality > 0m)
            .OrderByDescending(
                preference =>
                    preference.Quality)
            .ThenBy(
                preference =>
                    preference.Index);

    foreach (var preference in
             preferences)
    {
        if (preference.Language.Equals(
                "es",
                StringComparison.OrdinalIgnoreCase) ||
            preference.Language.StartsWith(
                "es-",
                StringComparison.OrdinalIgnoreCase))
        {
            return "es";
        }

        if (preference.Language.Equals(
                "en",
                StringComparison.OrdinalIgnoreCase) ||
            preference.Language.StartsWith(
                "en-",
                StringComparison.OrdinalIgnoreCase))
        {
            return "en-US";
        }
    }

    return "en-US";
}

static (
    string Language,
    decimal Quality,
    int Index)
    ParseLanguagePreference(
        string value,
        int index)
{
    var segments =
        value.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

    var language =
        segments.Length == 0
            ? string.Empty
            : segments[0];

    var quality =
        1m;

    foreach (var segment in
             segments.Skip(1))
    {
        if (!segment.StartsWith(
                "q=",
                StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (decimal.TryParse(
                segment[2..],
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsedQuality))
        {
            quality =
                Math.Clamp(
                    parsedQuality,
                    0m,
                    1m);
        }
    }

    return (
        language,
        quality,
        index);
}
