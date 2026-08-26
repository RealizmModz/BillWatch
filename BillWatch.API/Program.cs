using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Bills;
using BillWatch.API.Services.Plaid;
using Microsoft.AspNetCore.Identity;
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

builder.Services.Configure<PlaidOptions>(
    builder.Configuration.GetSection(
        "Plaid"));

builder.Services.AddDataProtection();

builder.Services.AddHttpClient<PlaidApiClient>();

builder.Services.AddScoped<PlaidTokenProtector>();

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
    RecurringBillDiscoveryPersistenceService>();

builder.Services.AddScoped<
    BillMonitoringRefreshService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapGroup("/api/auth")
    .MapIdentityApi<ApplicationUser>();

app.Run();