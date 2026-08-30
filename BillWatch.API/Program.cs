using System.Globalization;
using System.Security.Claims;
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

const string AuthenticationRateLimitPolicy =
    "authentication";

const string StatementUploadRateLimitPolicy =
    "statement-upload";

const string AccountExportRateLimitPolicy =
    "account-export";

const string StatementDownloadRateLimitPolicy =
    "statement-download";

var builder =
    WebApplication.CreateBuilder(
        args);

/*
 * Do not advertise the web server implementation.
 */
builder.WebHost.ConfigureKestrel(
    options =>
    {
        options.AddServerHeader =
            false;
    });

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
        "BillWatchDatabase");

if (string.IsNullOrWhiteSpace(
        connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'BillWatchDatabase' was not found.");
}

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

/*
 * Rate limiting is intentionally fail-closed.
 *
 * Authenticated requests use ownership-scoped user identifiers where
 * possible. Anonymous traffic falls back to the remote IP.
 *
 * Prefixing partition keys prevents a user identifier from ever colliding
 * with an IP address that happens to have the same textual representation.
 */
builder.Services.AddRateLimiter(
    options =>
    {
        options.RejectionStatusCode =
            StatusCodes.Status429TooManyRequests;

        options.OnRejected =
            static (
                context,
                _) =>
            {
                if (context.Lease.TryGetMetadata(
                        MetadataName.RetryAfter,
                        out var retryAfter))
                {
                    var seconds =
                        Math.Max(
                            1d,
                            Math.Ceiling(
                                retryAfter.TotalSeconds));

                    context.HttpContext.Response.Headers[
                        "Retry-After"] =
                        seconds.ToString(
                            CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

        options.GlobalLimiter =
            PartitionedRateLimiter.Create<
                HttpContext,
                string>(
                httpContext =>
                    CreateFixedWindowPartition(
                        GetRateLimitPartitionKey(
                            httpContext,
                            preferAuthenticatedUser:
                                true),
                        permitLimit:
                            300,
                        window:
                            TimeSpan.FromMinutes(
                                1)));

        /*
         * Authentication endpoints remain IP-partitioned.
         *
         * Requests are anonymous before a successful sign-in, so using a
         * claimed or supplied account identifier here would let a caller
         * choose their own limiter partition.
         */
        options.AddPolicy(
            AuthenticationRateLimitPolicy,
            httpContext =>
                CreateFixedWindowPartition(
                    GetRateLimitPartitionKey(
                        httpContext,
                        preferAuthenticatedUser:
                            false),
                    permitLimit:
                        20,
                    window:
                        TimeSpan.FromMinutes(
                            1)));

        options.AddPolicy(
            StatementUploadRateLimitPolicy,
            httpContext =>
                CreateFixedWindowPartition(
                    GetRateLimitPartitionKey(
                        httpContext,
                        preferAuthenticatedUser:
                            true),
                    permitLimit:
                        12,
                    window:
                        TimeSpan.FromMinutes(
                            10)));

        options.AddPolicy(
            AccountExportRateLimitPolicy,
            httpContext =>
                CreateFixedWindowPartition(
                    GetRateLimitPartitionKey(
                        httpContext,
                        preferAuthenticatedUser:
                            true),
                    permitLimit:
                        5,
                    window:
                        TimeSpan.FromHours(
                            1)));

        options.AddPolicy(
            StatementDownloadRateLimitPolicy,
            httpContext =>
                CreateFixedWindowPartition(
                    GetRateLimitPartitionKey(
                        httpContext,
                        preferAuthenticatedUser:
                            true),
                    permitLimit:
                        30,
                    window:
                        TimeSpan.FromMinutes(
                            10)));
    });

/*
 * Use the same application discriminator in every environment.
 *
 * Production additionally requires a persistent key location so encrypted
 * Plaid credentials and Identity/Data Protection material survive restarts.
 */
var dataProtectionBuilder =
    builder.Services
        .AddDataProtection()
        .SetApplicationName(
            "BillWatch");

var configuredDataProtectionPath =
    builder.Configuration[
        "DataProtection:KeysPath"];

if (!string.IsNullOrWhiteSpace(
        configuredDataProtectionPath))
{
    if (!builder.Environment.IsDevelopment() &&
        !Path.IsPathFullyQualified(
            configuredDataProtectionPath))
    {
        throw new InvalidOperationException(
            "DataProtection:KeysPath must be an absolute path outside development.");
    }

    var dataProtectionKeysPath =
        Path.GetFullPath(
            configuredDataProtectionPath);

    Directory.CreateDirectory(
        dataProtectionKeysPath);

    dataProtectionBuilder
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

if (!builder.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(
            configuredStatementStoragePath))
    {
        throw new InvalidOperationException(
            "BillStatementStorage:RootPath must be configured outside development.");
    }

    if (!Path.IsPathFullyQualified(
            configuredStatementStoragePath))
    {
        throw new InvalidOperationException(
            "BillStatementStorage:RootPath must be an absolute path outside development.");
    }
}

/*
 * Validate production-sensitive configuration before the host begins
 * accepting traffic.
 */
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

/*
 * Plaid options are validated on startup even during development.
 *
 * Missing local credentials remain allowed so developers can work on
 * non-Plaid areas, but an invalid environment value must never silently
 * fall back to another Plaid environment.
 */
builder.Services
    .AddOptions<PlaidOptions>()
    .Bind(
        builder.Configuration.GetSection(
            PlaidOptions.SectionName))
    .Validate(
        options =>
            string.Equals(
                options.Environment,
                "sandbox",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                options.Environment,
                "production",
                StringComparison.OrdinalIgnoreCase),
        "Plaid:Environment must be either 'sandbox' or 'production'.")
    .ValidateOnStart();

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
 * Deterministic extraction remains the only runtime extraction strategy
 * allowed to influence persistence.
 *
 * AI infrastructure exists for explicitly controlled evaluation, but it is
 * not registered as IBillStatementExtractionService.
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

/*
 * OpenAiBillStatementAiExtractor owns its request timeout with a linked
 * cancellation token.
 *
 * Disable HttpClient's independent 100-second timeout so two unrelated
 * timeout mechanisms cannot race each other and produce an unsanitized
 * cancellation path.
 */
builder.Services.AddHttpClient<
    OpenAiBillStatementAiExtractor>(
    client =>
    {
        client.Timeout =
            Timeout.InfiniteTimeSpan;
    });

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
    /*
     * OpenAPI is deliberately unavailable outside development.
     */
    app.MapOpenApi();
}
else
{
    /*
     * Production exception responses are generated through Problem Details
     * rather than exposing exception details to callers.
     */
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseWhen(
    context =>
        !context.Request.Path.StartsWithSegments(
            "/health"),
    branch =>
        branch.UseHttpsRedirection());

/*
 * Security and privacy headers are applied at response-start time so later
 * middleware or endpoints cannot accidentally replace BillWatch's required
 * values.
 */
app.Use(
    async (
        context,
        next) =>
    {
        context.Response.OnStarting(
            () =>
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

                context.Response.Headers[
                    "Content-Security-Policy"] =
                    "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

                context.Response.Headers[
                    "X-Permitted-Cross-Domain-Policies"] =
                    "none";

                /*
                 * Financial and authentication API responses must never be
                 * stored by browsers or intermediary caches.
                 *
                 * Health responses are also marked no-store so an
                 * orchestrator cannot receive a stale readiness result.
                 */
                if (context.Request.Path.StartsWithSegments(
                        "/api") ||
                    context.Request.Path.StartsWithSegments(
                        "/health"))
                {
                    context.Response.Headers[
                        "Cache-Control"] =
                        "no-store, no-cache, max-age=0, must-revalidate";

                    context.Response.Headers[
                        "Pragma"] =
                        "no-cache";

                    context.Response.Headers[
                        "Expires"] =
                        "0";
                }

                return Task.CompletedTask;
            });

        await next();
    });

/*
 * Authentication intentionally precedes named rate-limit policies because
 * sensitive BillWatch endpoints are partitioned by authenticated UserId.
 *
 * Anonymous callers still fall back to an IP-scoped partition.
 */
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
            var canConnect =
                await dbContext.Database.CanConnectAsync(
                    cancellationToken);

            return canConnect
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
            AuthenticationRateLimitPolicy);

authenticationGroup
    .MapIdentityApi<ApplicationUser>();

app.Run();

static string GetRateLimitPartitionKey(
    HttpContext httpContext,
    bool preferAuthenticatedUser)
{
    ArgumentNullException.ThrowIfNull(
        httpContext);

    if (preferAuthenticatedUser &&
        httpContext.User.Identity?.IsAuthenticated ==
            true)
    {
        var userId =
            httpContext.User.FindFirst(
                    ClaimTypes.NameIdentifier)?
                .Value;

        if (!string.IsNullOrWhiteSpace(
                userId))
        {
            return
                $"user:{userId}";
        }
    }

    var remoteIpAddress =
        httpContext.Connection
            .RemoteIpAddress?
            .ToString();

    /*
     * Use one shared fallback instead of a connection-specific value.
     * A per-connection fallback would let an abusive caller bypass the
     * limiter simply by opening new connections.
     */
    return string.IsNullOrWhiteSpace(
            remoteIpAddress)
        ? "ip:unknown"
        : $"ip:{remoteIpAddress}";
}

static RateLimitPartition<string>
    CreateFixedWindowPartition(
        string partitionKey,
        int permitLimit,
        TimeSpan window)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(
        partitionKey);

    if (permitLimit <=
        0)
    {
        throw new ArgumentOutOfRangeException(
            nameof(permitLimit));
    }

    if (window <=
        TimeSpan.Zero)
    {
        throw new ArgumentOutOfRangeException(
            nameof(window));
    }

    return RateLimitPartition.GetFixedWindowLimiter(
        partitionKey:
            partitionKey,

        factory:
            _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit =
                        permitLimit,

                    Window =
                        window,

                    QueueLimit =
                        0,

                    AutoReplenishment =
                        true
                });
}