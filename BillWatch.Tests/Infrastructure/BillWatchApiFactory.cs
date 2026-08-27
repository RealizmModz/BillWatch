using BillWatch.API;
using BillWatch.API.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BillWatch.Tests.Infrastructure;

public sealed class BillWatchApiFactory
    : WebApplicationFactory<ApiAssemblyMarker>
{
    private readonly string _databaseName =
        $"BillWatchSecurityTests-{Guid.NewGuid():N}";

    public HttpClient CreateHttpsClient()
    {
        return CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress =
                    new Uri(
                        "https://localhost"),

                AllowAutoRedirect =
                    false
            });
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment(
            "Development");

        builder.ConfigureAppConfiguration(
            (_, configuration) =>
            {
                var testSettings =
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:BillWatch"] =
                            "Host=localhost;Database=billwatch_tests;Username=test;Password=test",

                        ["Plaid:ClientId"] =
                            "test-client",

                        ["Plaid:Secret"] =
                            "test-secret",

                        ["Plaid:Environment"] =
                            "sandbox"
                    };

                configuration.AddInMemoryCollection(
                    testSettings);
            });

        builder.ConfigureServices(
            services =>
            {
                services.RemoveAll<
                    IDbContextOptionsConfiguration<
                        BillWatchDbContext>>();

                services.RemoveAll<
                    DbContextOptions<
                        BillWatchDbContext>>();

                services.RemoveAll<
                    BillWatchDbContext>();

                services.AddDbContext<
                    BillWatchDbContext>(
                    options =>
                        options.UseInMemoryDatabase(
                            _databaseName));
            });
    }
}