using System.Threading.RateLimiting;
using System.Net;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Infrastructure;
using BillWatch.API.Services.Bills;
using BillWatch.API.Services.Plaid;
using BillWatch.API.Services.Statements;
using BillWatch.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder =
    WebApplication.CreateBuilder(
        args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var configuredKnownProxies =
    builder.Configuration
        .GetSection(
            "ReverseProxy:KnownProxies")
        .Get<string[]>()
    ?? [];

var useForwardedHeaders =
    configuredKnownProxies.Length > 0;

if (useForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(
        options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedProto;

            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            foreach (var configuredProxy in
                     configuredKnownProxies)
            {
                if (!IPAddress.TryParse(
                        configuredProxy,
                        out var proxyAddress))
                {
                    throw new InvalidOperationException(
                        "ReverseProxy:KnownProxies contains an invalid IP address.");
                }

                options.KnownProxies.Add(
                    proxyAddress);
            }
        });
}

var connectionString =
    builder.Configuration.GetConnectionString(
        "BillWatchDatabase")
    ?? throw new InvalidOperationException(
        "Connection string 'BillWatchDatabase' was not found.");

builder.Services.AddDbContext<BillWatchDbContext>(
    options =>
        options.UseNpgsql(
            connectionString));

builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>()
    .AddEntityFrameworkStores<BillWatchDbContext>();

builder.Services.Configure<IdentityOptions>(
    options =>
    {
        options.User.RequireUniqueEmail =
            true;

        options.Password.RequiredLength =
            12;

        options.Password.RequiredUniqueChars =
            4;

        options.Password.RequireDigit =
            true;

        options.Password.RequireLowercase =
            true;

        options.Password.RequireUppercase =
            true;

        options.Password.RequireNonAlphanumeric =
            true;

        options.Lockout.AllowedForNewUsers =
            true;

        options.Lockout.MaxFailedAccessAttempts =
            5;

        options.Lockout.DefaultLockoutTimeSpan =
            TimeSpan.FromMinutes(
                15);
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(
    options =>
    {
        options.RejectionStatusCode =
            StatusCodes.Status429TooManyRequests;

        options.GlobalLimiter =
            PartitionedRateLimiter.Create<
                HttpContext,
                string>(
                httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey:
                            httpContext.Connection
                                .RemoteIpAddress?
                                .ToString()
                            ?? "unknown",

                        factory:
                            _ =>
                                new FixedWindowRateLimiterOptions
                                {
                                    PermitLimit =
                                        300,

                                    Window =
                                        TimeSpan.FromMinutes(
                                            1),

                                    QueueLimit =
                                        0,

                                    AutoReplenishment =
                                        true
                                }));

        options.AddPolicy(
            "authentication",
            httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey:
                        httpContext.Connection
                            .RemoteIpAddress?
                            .ToString()
                        ?? "unknown",

                    factory:
                        _ =>
                            new FixedWindowRateLimiterOptions
                            {
                                PermitLimit =
                                    20,

                                Window =
                                    TimeSpan.FromMinutes(
                                        1),

                                QueueLimit =
                                    0,

                                AutoReplenishment =
                                    true
                            }));

        options.AddPolicy(
            "statement-upload",
            httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey:
                        httpContext.User.FindFirst(
                            System.Security.Claims.ClaimTypes.NameIdentifier)?
                            .Value
                        ?? httpContext.Connection
                            .RemoteIpAddress?
                            .ToString()
                        ?? "unknown",

                    factory:
                        _ =>
                            new FixedWindowRateLimiterOptions
                            {
                                PermitLimit =
                                    12,

                                Window =
                                    TimeSpan.FromMinutes(
                                        10),

                                QueueLimit =
                                    0,

                                AutoReplenishment =
                                    true
                            }));

        options.AddPolicy(
            "account-export",
            httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey:
                        httpContext.User.FindFirst(
                            System.Security.Claims.ClaimTypes.NameIdentifier)?
                            .Value
                        ?? httpContext.Connection
                            .RemoteIpAddress?
                            .ToString()
                        ?? "unknown",

                    factory:
                        _ =>
                            new FixedWindowRateLimiterOptions
                            {
                                PermitLimit =
                                    5,

                                Window =
                                    TimeSpan.FromHours(
                                        1),

                                QueueLimit =
                                    0,

                                AutoReplenishment =
                                    true
                            }));

        options.AddPolicy(
            "statement-download",
            httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey:
                        httpContext.User.FindFirst(
                            System.Security.Claims.ClaimTypes.NameIdentifier)?
                            .Value
                        ?? httpContext.Connection
                            .RemoteIpAddress?
                            .ToString()
                        ?? "unknown",

                    factory:
                        _ =>
                            new FixedWindowRateLimiterOptions
                            {
                                PermitLimit =
                                    30,

                                Window =
                                    TimeSpan.FromMinutes(
                                        10),

                                QueueLimit =
                                    0,

                                AutoReplenishment =
                                    true
                            }));
    });

var dataProtectionBuilder =
    builder.Services.AddDataProtection();

var configuredDataProtectionPath =
    builder.Configuration[
        "DataProtection:KeysPath"];

if (!string.IsNullOrWhiteSpace(
        configuredDataProtectionPath))
{
    var dataProtectionKeysPath =
        Path.GetFullPath(
            configuredDataProtectionPath);

    Directory.CreateDirectory(
        dataProtectionKeysPath);

    dataProtectionBuilder
        .SetApplicationName(
            "BillWatch")
        .PersistKeysToFileSystem(
            new DirectoryInfo(
                dataProtectionKeysPath));
}
else if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "DataProtection:KeysPath must be configured outside development.");
}

var configuredStatementStoragePath =
    builder.Configuration[
        $"{BillStatementStorageOptions.SectionName}:RootPath"];

if (!builder.Environment.IsDevelopment() &&
    string.IsNullOrWhiteSpace(
        configuredStatementStoragePath))
{
    throw new InvalidOperationException(
        "BillStatementStorage:RootPath must be configured outside development.");
}

if (!builder.Environment.IsDevelopment())
{
    var plaidClientId =
        builder.Configuration[
            $"{PlaidOptions.SectionName}:ClientId"];

    var plaidSecret =
        builder.Configuration[
            $"{PlaidOptions.SectionName}:Secret"];

    if (string.IsNullOrWhiteSpace(
            plaidClientId) ||
        string.IsNullOrWhiteSpace(
            plaidSecret))
    {
        throw new InvalidOperationException(
            "Plaid credentials must be configured outside development.");
    }

    var allowedHosts =
        builder.Configuration[
            "AllowedHosts"];

    if (string.IsNullOrWhiteSpace(
            allowedHosts) ||
        string.Equals(
            allowedHosts.Trim(),
            "*",
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "AllowedHosts must be explicitly configured outside development.");
    }
}

builder.Services.Configure<PlaidOptions>(
    builder.Configuration.GetSection(
        PlaidOptions.SectionName));

builder.Services.AddHttpClient<PlaidApiClient>();

builder.Services.AddScoped<
    PlaidTokenProtector>();

builder.Services.AddScoped<
    PlaidConnectionExchangeService>();

builder.Services.AddScoped<
    PlaidLinkService>();

builder.Services.AddScoped<
    PlaidHostedLinkCompletionService>();

builder.Services.AddScoped<
    PlaidAccountSyncService>();

builder.Services.AddScoped<
    PlaidTransactionSyncService>();

builder.Services.AddScoped<
    PlaidConnectionDisconnectService>();

builder.Services.AddScoped<
    RecurringBillDiscoveryPersistenceService>();

builder.Services.AddScoped<
    BankConnectionHealthAlertService>();

builder.Services.AddScoped<
    BillMonitoringRefreshService>();

builder.Services.Configure<
    BillMonitoringBackgroundOptions>(
    builder.Configuration.GetSection(
        BillMonitoringBackgroundOptions.SectionName));

builder.Services.AddScoped<
    BillMonitoringRefreshScheduler>();

builder.Services.AddHostedService<
    BillMonitoringBackgroundService>();

builder.Services.Configure<BillStatementStorageOptions>(
    builder.Configuration.GetSection(
        BillStatementStorageOptions.SectionName));

builder.Services.Configure<BillStatementOcrOptions>(
    builder.Configuration.GetSection(
        BillStatementOcrOptions.SectionName));

builder.Services.AddScoped<
    SecureBillStatementStorageService>();

builder.Services.AddScoped<
    PdfBillStatementTextExtractor>();

builder.Services.AddSingleton<
    IBillStatementOcrEngine,
    TesseractBillStatementOcrEngine>();

builder.Services.AddScoped<
    BillStatementDocumentTextReader>();

/*
 * Deterministic extraction remains the active implementation today.
 *
 * Statement processing depends only on the extraction interface, so a
 * future AI-assisted/provider/hybrid implementation can replace or
 * supplement this without changing controllers or the MAUI client.
 */
builder.Services.AddSingleton<
    DeterministicBillStatementParser>();

builder.Services.AddSingleton<
    DeterministicBillLineItemParser>();

builder.Services.AddSingleton<
    IBillStatementExtractionService,
    DeterministicBillStatementExtractionService>();

builder.Services.AddSingleton<
    BillStatementAiCandidateValidator>();

builder.Services.AddSingleton<
    BillStatementAiCandidateConversionService>();

builder.Services
    .AddOptions<OpenAiBillStatementOptions>()
    .Bind(
        builder.Configuration.GetSection(
            OpenAiBillStatementOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<
    IValidateOptions<OpenAiBillStatementOptions>,
    OpenAiBillStatementOptionsValidator>();

builder.Services
    .AddOptions<BillStatementAiShadowOptions>()
    .Bind(
        builder.Configuration.GetSection(
            BillStatementAiShadowOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<
    IValidateOptions<BillStatementAiShadowOptions>,
    BillStatementAiShadowOptionsValidator>();

builder.Services.AddHttpClient<
    OpenAiBillStatementAiExtractor>();

builder.Services.AddTransient<
    IBillStatementAiExtractor>(
    serviceProvider =>
        serviceProvider.GetRequiredService<
            OpenAiBillStatementAiExtractor>());

builder.Services.AddSingleton<
    BillStatementValidationService>();

builder.Services.AddSingleton<
    BillComparisonService>();

builder.Services.AddScoped<
    BillStatementEvidenceAlertService>();

builder.Services.AddScoped<
    BillStatementChangeDetectionService>();

builder.Services.AddScoped<
    BillStatementPaymentDueAlertService>();

builder.Services.AddScoped<
    BillStatementPersistenceService>();

builder.Services.AddScoped<
    BillStatementProcessingService>();

builder.Services.AddScoped<
    BillWatchReadinessService>();

builder.Services.AddSingleton<
    BillStatementProcessingSignal>();

builder.Services.AddHostedService<
    BillStatementProcessingBackgroundService>();

var app =
    builder.Build();

if (builder.Configuration.GetValue<bool>(
        "Database:MigrateOnStartup"))
{
    await using var migrationScope =
        app.Services.CreateAsyncScope();

    var migrationDbContext =
        migrationScope.ServiceProvider
            .GetRequiredService<BillWatchDbContext>();

    await migrationDbContext.Database.MigrateAsync();
}

if (useForwardedHeaders)
{
    app.UseForwardedHeaders();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseWhen(
    context =>
        !context.Request.Path.StartsWithSegments(
            "/health"),
    branch =>
        branch.UseHttpsRedirection());

app.Use(
    async (context, next) =>
    {
        context.Response.Headers[
            "X-Content-Type-Options"] =
            "nosniff";

        context.Response.Headers[
            "X-Frame-Options"] =
            "DENY";

        context.Response.Headers[
            "Referrer-Policy"] =
            "no-referrer";

        context.Response.Headers[
            "Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=()";

        if (context.Request.Path.StartsWithSegments(
                "/api"))
        {
            context.Response.Headers[
                "Cache-Control"] =
                "no-store, max-age=0";

            context.Response.Headers[
                "Pragma"] =
                "no-cache";
        }

        await next();
    });

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet(
        "/health/live",
        () =>
            Results.Ok(
                new
                {
                    status =
                        "live"
                }))
    .AllowAnonymous();

app.MapGet(
        "/health/ready",
        async (
            BillWatchReadinessService readinessService,
            CancellationToken cancellationToken) =>
        {
            var isReady =
                await readinessService.IsReadyAsync(
                    cancellationToken);

            return isReady
                ? Results.Ok(
                    new
                    {
                        status =
                            "ready"
                    })
                : Results.StatusCode(
                    StatusCodes.Status503ServiceUnavailable);
        })
    .AllowAnonymous();

app.MapControllers();

var authenticationGroup =
    app.MapGroup(
            "/api/auth")
        .RequireRateLimiting(
            "authentication");

authenticationGroup
    .MapIdentityApi<ApplicationUser>();

app.Run();
