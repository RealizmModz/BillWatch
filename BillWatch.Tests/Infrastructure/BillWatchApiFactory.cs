using BillWatch.API;
using BillWatch.API.Data;
using BillWatch.API.Services.Statements;
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
    private readonly bool _subscriptionEnforcementEnabled;
    private readonly bool _stripeBillingConfigured;

    public BillWatchApiFactory()
    {
    }

    private BillWatchApiFactory(
        bool subscriptionEnforcementEnabled,
        bool stripeBillingConfigured)
    {
        _subscriptionEnforcementEnabled =
            subscriptionEnforcementEnabled;

        _stripeBillingConfigured =
            stripeBillingConfigured;
    }

    public static BillWatchApiFactory WithSubscriptionEnforcement()
    {
        return new BillWatchApiFactory(
            subscriptionEnforcementEnabled: true,
            stripeBillingConfigured: false);
    }

    public static BillWatchApiFactory WithStripeBilling()
    {
        return new BillWatchApiFactory(
            subscriptionEnforcementEnabled: false,
            stripeBillingConfigured: true);
    }

    private readonly string _databaseName =
        $"BillWatchSecurityTests-{Guid.NewGuid():N}";

    private readonly string _statementStorageRoot =
        Path.Combine(
            Path.GetTempPath(),
            "BillWatch.Tests",
            Guid.NewGuid().ToString("N"));

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
        /*
         * Program reads the connection string while constructing the app.
         * Set this at the web-host layer as well as in app configuration so
         * tests never depend on developer user secrets.
         */
        builder.UseSetting(
            "ConnectionStrings:BillWatchDatabase",
            "Host=localhost;Database=billwatch_tests;Username=test;Password=test");

        builder.UseEnvironment(
            "Development");

        builder.ConfigureAppConfiguration(
            (_, configuration) =>
            {
                var testSettings =
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:BillWatchDatabase"] =
                            "Host=localhost;Database=billwatch_tests;Username=test;Password=test",

                        ["Plaid:ClientId"] =
                            "test-client",

                        ["Plaid:Secret"] =
                            "test-secret",

                        ["Plaid:Environment"] =
                            "sandbox",

                        ["BillStatementStorage:RootPath"] =
                            _statementStorageRoot,

                        ["BillStatementStorage:MaxFileSizeBytes"] =
                            (15L * 1024 * 1024)
                                .ToString(),

                        /*
                         * Never let integration tests launch the real
                         * scheduled Plaid monitoring worker.
                         */
                        ["BillMonitoring:BackgroundRefresh:Enabled"] =
                            "false",

                        ["Subscription:EnforcementEnabled"] =
                            _subscriptionEnforcementEnabled.ToString(),

                        ["StripeBilling:Enabled"] =
                            _stripeBillingConfigured.ToString(),

                        ["StripeBilling:SecretKey"] =
                            _stripeBillingConfigured
                                ? "sk_test_billwatch"
                                : string.Empty,

                        ["StripeBilling:WebhookSecret"] =
                            _stripeBillingConfigured
                                ? "whsec_billwatch_test"
                                : string.Empty,

                        ["StripeBilling:MonthlyPriceId"] =
                            _stripeBillingConfigured
                                ? "price_billwatch_monthly_test"
                                : string.Empty,

                        ["StripeBilling:YearlyPriceId"] =
                            _stripeBillingConfigured
                                ? "price_billwatch_yearly_test"
                                : string.Empty,

                        ["StripeBilling:PublicWebBaseUrl"] =
                            "https://billbeacon.net"
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

                /*
                 * Routine tests do not load native Tesseract.
                 *
                 * Native OCR tests explicitly replace this fake with
                 * the production engine.
                 */
                services.RemoveAll<
                    IBillStatementOcrEngine>();

                services.AddSingleton<
                    IBillStatementOcrEngine,
                    TestBillStatementOcrEngine>();
            });
    }

    protected override void Dispose(
        bool disposing)
    {
        base.Dispose(
            disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            if (Directory.Exists(
                    _statementStorageRoot))
            {
                Directory.Delete(
                    _statementStorageRoot,
                    recursive:
                        true);
            }
        }
        catch
        {
            // Test cleanup must not hide a real test failure.
        }
    }

    private sealed class TestBillStatementOcrEngine
        : IBillStatementOcrEngine
    {
        public BillStatementOcrResult TryExtract(
            Stream source,
            string mediaType,
            string fileExtension)
        {
            ArgumentNullException.ThrowIfNull(
                source);

            return BillStatementOcrResult.Failure(
                pageCount:
                    1);
        }
    }
}
