using System.Threading.RateLimiting;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Bills;
using BillWatch.API.Services.Plaid;
using BillWatch.API.Services.Statements;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

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
            TimeSpan.FromMinutes(15);
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(
    options =>
    {
        options.RejectionStatusCode =
            StatusCodes.Status429TooManyRequests;

        options.GlobalLimiter =
            PartitionedRateLimiter.Create<HttpContext, string>(
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
                                    PermitLimit = 300,
                                    Window =
                                        TimeSpan.FromMinutes(1),
                                    QueueLimit = 0,
                                    AutoReplenishment = true
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
                                PermitLimit = 20,
                                Window =
                                    TimeSpan.FromMinutes(1),
                                QueueLimit = 0,
                                AutoReplenishment = true
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

builder.Services.Configure<PlaidOptions>(
    builder.Configuration.GetSection(
        "Plaid"));

builder.Services.AddHttpClient<PlaidApiClient>();

builder.Services.AddScoped<PlaidTokenProtector>();
builder.Services.AddScoped<PlaidConnectionExchangeService>();
builder.Services.AddScoped<PlaidLinkService>();
builder.Services.AddScoped<PlaidHostedLinkCompletionService>();
builder.Services.AddScoped<PlaidAccountSyncService>();
builder.Services.AddScoped<PlaidTransactionSyncService>();
builder.Services.AddScoped<PlaidConnectionDisconnectService>();

builder.Services.AddScoped<RecurringBillDiscoveryPersistenceService>();
builder.Services.AddScoped<BillMonitoringRefreshService>();

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

builder.Services.Configure<BillStatementStorageOptions>(
    builder.Configuration.GetSection(
        BillStatementStorageOptions.SectionName));

builder.Services.AddScoped<
    SecureBillStatementStorageService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();

var authenticationGroup =
    app.MapGroup("/api/auth")
        .RequireRateLimiting(
            "authentication");

authenticationGroup
    .MapIdentityApi<ApplicationUser>();

app.Run();