using System.Threading.RateLimiting;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Bills;
using BillWatch.API.Services.Plaid;
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

builder.Services.Configure<PlaidOptions>(
    builder.Configuration.GetSection(
        "Plaid"));

builder.Services.AddDataProtection();

builder.Services.AddHttpClient<PlaidApiClient>();

builder.Services.AddScoped<PlaidTokenProtector>();
builder.Services.AddScoped<PlaidConnectionExchangeService>();
builder.Services.AddScoped<PlaidLinkService>();
builder.Services.AddScoped<PlaidHostedLinkCompletionService>();
builder.Services.AddScoped<PlaidAccountSyncService>();
builder.Services.AddScoped<PlaidTransactionSyncService>();
builder.Services.AddScoped<RecurringBillDiscoveryPersistenceService>();
builder.Services.AddScoped<BillMonitoringRefreshService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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