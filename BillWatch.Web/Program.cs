using BillWatch.Web.Components;
using BillWatch.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;

const long StatementFileSizeLimit =
    15L * 1024 * 1024;

const long StatementMultipartBodyLimit =
    16L * 1024 * 1024;

var builder =
    WebApplication.CreateBuilder(args);

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

var apiBaseUrl =
    builder.Configuration[
        "BillWatchApi:BaseUrl"];

if (string.IsNullOrWhiteSpace(
        apiBaseUrl))
{
    if (!builder.Environment
        .IsDevelopment())
    {
        throw new InvalidOperationException(
            "BillWatchApi:BaseUrl must be configured outside development.");
    }

    apiBaseUrl =
        "https://localhost:7243";
}

builder.Services.AddHttpClient(
    "BillWatchApi",
    client =>
    {
        client.BaseAddress =
            new Uri(apiBaseUrl);
    });

builder.Services.AddScoped<
    WebAuthenticationService>();

builder.Services.AddScoped<
    BillWatchBffProxyService>();

var app =
    builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(
        "/Error",
        createScopeForErrors: true);

    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapPost(
    "/auth/login",
    async (
        HttpContext context,
        IAntiforgery antiforgery,
        WebAuthenticationService authenticationService) =>
    {
        await antiforgery
            .ValidateRequestAsync(
                context);

        var form =
            await context.Request
                .ReadFormAsync(
                    context.RequestAborted);

        var email =
            form["email"]
                .ToString()
                .Trim();

        var password =
            form["password"]
                .ToString();

        var rememberMe =
            string.Equals(
                form["rememberMe"],
                "on",
                StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(
                email) ||
            string.IsNullOrWhiteSpace(
                password))
        {
            return Results.Redirect(
                "/login?error=" +
                Uri.EscapeDataString(
                    "Enter your email and password."));
        }

        var result =
            await authenticationService
                .LoginAsync(
                    context,
                    email,
                    password,
                    rememberMe,
                    context.RequestAborted);

        if (!result.Succeeded)
        {
            return Results.Redirect(
                "/login?error=" +
                Uri.EscapeDataString(
                    result.ErrorMessage ??
                    "Unable to sign in."));
        }

        return Results.Redirect(
            "/app");
    });

app.MapPost(
    "/auth/register",
    async (
        HttpContext context,
        IAntiforgery antiforgery,
        WebAuthenticationService authenticationService) =>
    {
        await antiforgery
            .ValidateRequestAsync(
                context);

        var form =
            await context.Request
                .ReadFormAsync(
                    context.RequestAborted);

        var email =
            form["email"]
                .ToString()
                .Trim();

        var password =
            form["password"]
                .ToString();

        var confirmPassword =
            form["confirmPassword"]
                .ToString();

        if (string.IsNullOrWhiteSpace(
                email))
        {
            return Results.Redirect(
                "/register?error=" +
                Uri.EscapeDataString(
                    "Enter an email address."));
        }

        if (string.IsNullOrWhiteSpace(
                password))
        {
            return Results.Redirect(
                "/register?error=" +
                Uri.EscapeDataString(
                    "Create a password."));
        }

        if (!string.Equals(
                password,
                confirmPassword,
                StringComparison.Ordinal))
        {
            return Results.Redirect(
                "/register?error=" +
                Uri.EscapeDataString(
                    "The passwords do not match."));
        }

        var result =
            await authenticationService
                .RegisterAsync(
                    context,
                    email,
                    password,
                    context.RequestAborted);

        if (!result.Succeeded)
        {
            return Results.Redirect(
                "/register?error=" +
                Uri.EscapeDataString(
                    result.ErrorMessage ??
                    "Unable to create your account."));
        }

        return Results.Redirect(
            "/app");
    });

app.MapPost(
    "/auth/logout",
    async (
        HttpContext context,
        IAntiforgery antiforgery,
        WebAuthenticationService authenticationService) =>
    {
        await antiforgery
            .ValidateRequestAsync(
                context);

        await authenticationService
            .LogoutAsync(
                context);

        return Results.Redirect(
            "/");
    });

var bff =
    app.MapGroup(
            "/bff")
        .RequireAuthorization();

bff.MapGet(
    "/antiforgery",
    (
        HttpContext context,
        IAntiforgery antiforgery) =>
    {
        var tokens =
            antiforgery.GetAndStoreTokens(
                context);

        if (string.IsNullOrWhiteSpace(
                tokens.RequestToken))
        {
            return Results.Problem(
                statusCode:
                    StatusCodes
                        .Status500InternalServerError);
        }

        return Results.Ok(
            new
            {
                requestToken =
                    tokens.RequestToken
            });
    });

bff.MapGet(
    "/bill-streams",
    async (
        HttpContext context,
        BillWatchBffProxyService proxyService) =>
    {
        return await proxyService
            .ForwardGetAsync(
                context,
                "/api/bill-streams",
                context.RequestAborted);
    });

bff.MapGet(
    "/bill-streams/{billStreamId:guid}",
    async (
        HttpContext context,
        BillWatchBffProxyService proxyService,
        Guid billStreamId) =>
    {
        if (billStreamId ==
            Guid.Empty)
        {
            return Results.NotFound();
        }

        return await proxyService
            .ForwardGetAsync(
                context,
                $"/api/bill-streams/{billStreamId}",
                context.RequestAborted);
    });

bff.MapGet(
    "/bank-accounts",
    async (
        HttpContext context,
        BillWatchBffProxyService proxyService) =>
    {
        return await proxyService
            .ForwardGetAsync(
                context,
                "/api/bank-accounts",
                context.RequestAborted);
    });

bff.MapGet(
    "/bank-connections",
    async (
        HttpContext context,
        BillWatchBffProxyService proxyService) =>
    {
        return await proxyService
            .ForwardGetAsync(
                context,
                "/api/bank-connections",
                context.RequestAborted);
    });

bff.MapGet(
    "/bank-transactions",
    async (
        HttpContext context,
        BillWatchBffProxyService proxyService,
        int? take) =>
    {
        var safeTake =
            Math.Clamp(
                take ?? 100,
                1,
                500);

        return await proxyService
            .ForwardGetAsync(
                context,
                $"/api/bank-transactions?take={safeTake}",
                context.RequestAborted);
    });

bff.MapGet(
    "/alerts",
    async (
        HttpContext context,
        BillWatchBffProxyService proxyService,
        bool? includeDismissed,
        bool? unreadOnly,
        int? take) =>
    {
        var safeTake =
            Math.Clamp(
                take ?? 50,
                1,
                100);

        var includeDismissedValue =
            includeDismissed ??
            false;

        var unreadOnlyValue =
            unreadOnly ??
            false;

        var requestUri =
            "/api/alerts" +
            $"?includeDismissed={includeDismissedValue.ToString().ToLowerInvariant()}" +
            $"&unreadOnly={unreadOnlyValue.ToString().ToLowerInvariant()}" +
            $"&take={safeTake}";

        return await proxyService
            .ForwardGetAsync(
                context,
                requestUri,
                context.RequestAborted);
    });

bff.MapPost(
    "/alerts/{alertId:guid}/read",
    async (
        HttpContext context,
        IAntiforgery antiforgery,
        BillWatchBffProxyService proxyService,
        Guid alertId) =>
    {
        await antiforgery
            .ValidateRequestAsync(
                context);

        if (alertId ==
            Guid.Empty)
        {
            return Results.NotFound();
        }

        return await proxyService
            .ForwardPostAsync(
                context,
                $"/api/alerts/{alertId}/read",
                includeEmptyJsonBody:
                    false,
                context.RequestAborted);
    });

bff.MapPost(
    "/alerts/{alertId:guid}/dismiss",
    async (
        HttpContext context,
        IAntiforgery antiforgery,
        BillWatchBffProxyService proxyService,
        Guid alertId) =>
    {
        await antiforgery
            .ValidateRequestAsync(
                context);

        if (alertId ==
            Guid.Empty)
        {
            return Results.NotFound();
        }

        return await proxyService
            .ForwardPostAsync(
                context,
                $"/api/alerts/{alertId}/dismiss",
                includeEmptyJsonBody:
                    false,
                context.RequestAborted);
    });

bff.MapGet(
    "/account/export",
    async (
        HttpContext context,
        BillWatchBffProxyService proxyService) =>
    {
        return await proxyService
            .ForwardDownloadAsync(
                context,
                "/api/account/export",
                "billwatch-data-export.json",
                "application/json; charset=utf-8",
                context.RequestAborted);
    });

bff.MapDelete(
    "/account",
    async (
        HttpContext context,
        IAntiforgery antiforgery,
        BillWatchBffProxyService proxyService) =>
    {
        await antiforgery
            .ValidateRequestAsync(
                context);

        var confirmation =
            context.Request.Headers[
                "X-BillWatch-Delete-Confirmation"]
                .ToString();

        if (!string.Equals(
                confirmation,
                "DELETE",
                StringComparison.Ordinal))
        {
            return Results.BadRequest(
                new
                {
                    message =
                        "Type DELETE to confirm permanent account deletion."
                });
        }

        return await proxyService
            .DeleteAccountAsync(
                context,
                context.RequestAborted);
    });

bff.MapPost(
    "/plaid/link-session",
    async (
        HttpContext context,
        IAntiforgery antiforgery,
        BillWatchBffProxyService proxyService) =>
    {
        await antiforgery
            .ValidateRequestAsync(
                context);

        return await proxyService
            .ForwardPostAsync(
                context,
                "/api/plaid/link-token",
                includeEmptyJsonBody:
                    true,
                context.RequestAborted);
    });

bff.MapPost(
    "/plaid/connections/{connectionId:guid}/update-link-session",
    async (
        HttpContext context,
        IAntiforgery antiforgery,
        BillWatchBffProxyService proxyService,
        Guid connectionId) =>
    {
        await antiforgery
            .ValidateRequestAsync(
                context);

        if (connectionId ==
            Guid.Empty)
        {
            return Results.NotFound();
        }

        return await proxyService
            .ForwardPostAsync(
                context,
                $"/api/plaid/connections/{connectionId}/update-link-token",
                includeEmptyJsonBody:
                    false,
                context.RequestAborted);
    });

bff.MapPost(
    "/plaid/link-session/{sessionId:guid}/complete",
    async (
        HttpContext context,
        IAntiforgery antiforgery,
        BillWatchBffProxyService proxyService,
        Guid sessionId) =>
    {
        await antiforgery
            .ValidateRequestAsync(
                context);

        if (sessionId ==
            Guid.Empty)
        {
            return Results.NotFound();
        }

        return await proxyService
            .ForwardPostAsync(
                context,
                $"/api/plaid/link-session/{sessionId}/complete",
                includeEmptyJsonBody:
                    false,
                context.RequestAborted);
    });

bff.MapDelete(
    "/bank-connections/{connectionId:guid}",
    async (
        HttpContext context,
        IAntiforgery antiforgery,
        BillWatchBffProxyService proxyService,
        Guid connectionId) =>
    {
        await antiforgery
            .ValidateRequestAsync(
                context);

        if (connectionId ==
            Guid.Empty)
        {
            return Results.NotFound();
        }

        return await proxyService
            .ForwardDeleteAsync(
                context,
                $"/api/bank-connections/{connectionId}",
                context.RequestAborted);
    });

bff.MapPost(
    "/bill-streams/{billStreamId:guid}/statement-uploads",
    async (
        HttpContext context,
        IAntiforgery antiforgery,
        BillWatchBffProxyService proxyService,
        Guid billStreamId) =>
    {
        await antiforgery
            .ValidateRequestAsync(
                context);

        if (billStreamId ==
            Guid.Empty)
        {
            return Results.NotFound();
        }

        if (context.Request.ContentLength is >
            StatementMultipartBodyLimit)
        {
            return Results.StatusCode(
                StatusCodes
                    .Status413PayloadTooLarge);
        }

        IFormCollection form;

        try
        {
            form =
                await context.Request
                    .ReadFormAsync(
                        context.RequestAborted);
        }
        catch (InvalidDataException)
        {
            return Results.StatusCode(
                StatusCodes
                    .Status413PayloadTooLarge);
        }

        var file =
            form.Files.GetFile(
                "file");

        if (file is null)
        {
            return Results.BadRequest(
                new
                {
                    message =
                        "Select a bill statement to upload."
                });
        }

        if (file.Length <= 0)
        {
            return Results.BadRequest(
                new
                {
                    message =
                        "The selected statement is empty."
                });
        }

        if (file.Length >
            StatementFileSizeLimit)
        {
            return Results.BadRequest(
                new
                {
                    message =
                        "Bill statements must be 15 MB or smaller."
                });
        }

        return await proxyService
            .ForwardMultipartFileAsync(
                context,
                $"/api/bill-streams/{billStreamId}/statement-uploads",
                file,
                context.RequestAborted);
    });

bff.MapGet(
    "/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}",
    async (
        HttpContext context,
        BillWatchBffProxyService proxyService,
        Guid billStreamId,
        Guid uploadId) =>
    {
        if (billStreamId ==
                Guid.Empty ||
            uploadId ==
                Guid.Empty)
        {
            return Results.NotFound();
        }

        return await proxyService
            .ForwardGetAsync(
                context,
                $"/api/bill-streams/{billStreamId}/statement-uploads/{uploadId}",
                context.RequestAborted);
    });

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();