using BillWatch.Web.Components;
using BillWatch.Web.Infrastructure;
using BillWatch.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;

const long StatementMultipartBodyLimit =
    16L * 1024 * 1024;

var builder =
    WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(
    options =>
    {
        options.AddServerHeader =
            false;
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
app.UseAntiforgery();

app.MapStaticAssets();

app.MapBillWatchHealthEndpoints();
app.MapBillWatchAuthEndpoints();
app.MapBillWatchBffEndpoints();
app.MapBillWatchAdminBffEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();