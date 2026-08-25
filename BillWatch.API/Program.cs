using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddDataProtection();
builder.Services.AddAuthorization();

var connectionString =
    builder.Configuration.GetConnectionString("BillWatchDatabase")
    ?? throw new InvalidOperationException(
        "The BillWatchDatabase connection string is not configured.");

builder.Services.AddDbContext<BillWatchDbContext>(
    options =>
        options.UseNpgsql(connectionString));

builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>(
        options =>
        {
            options.User.RequireUniqueEmail = true;

            options.Password.RequiredLength = 10;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;

            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan =
                TimeSpan.FromMinutes(15);
        })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<BillWatchDbContext>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGroup("/api/auth")
    .MapIdentityApi<ApplicationUser>();

app.Run();