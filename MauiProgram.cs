using System.Reflection;
using BillWatch.Core.Configuration;
using BillWatch.Services;
using BillWatch.ViewModels;
using Microsoft.Extensions.Logging;

namespace BillWatch;

public static class MauiProgram
{
    private const string ApiBaseUrlMetadataName =
        "BillWatchApiBaseUrl";

    public static MauiApp CreateMauiApp()
    {
        var builder =
            MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(
                fonts =>
                {
                    fonts.AddFont(
                        "OpenSans-Regular.ttf",
                        "OpenSansRegular");

                    fonts.AddFont(
                        "OpenSans-Semibold.ttf",
                        "OpenSansSemibold");
                });

        builder.Services.AddSingleton(
            new HttpClient
            {
                BaseAddress =
                    GetApiBaseAddress()
            });

        builder.Services.AddSingleton<BillWatchApiClient>();
        builder.Services.AddSingleton<AuthSession>();
        builder.Services.AddSingleton<AuthenticationService>();
        builder.Services.AddSingleton<BillStreamService>();
        builder.Services.AddSingleton<PlaidConnectionService>();
        builder.Services.AddSingleton<BankDataService>();

        builder.Services.AddSingleton<BillAlertService>();

        builder.Services.AddTransient<LoginPageViewModel>();
        builder.Services.AddTransient<LoginPage>();

        builder.Services.AddTransient<MainPageViewModel>();
        builder.Services.AddTransient<MainPage>();

        builder.Services.AddTransient<BillsPageViewModel>();
        builder.Services.AddTransient<BillsPage>();

        builder.Services.AddTransient<ConnectBankPageViewModel>();
        builder.Services.AddTransient<ConnectBankPage>();

        builder.Services.AddTransient<TransactionsPageViewModel>();
        builder.Services.AddTransient<TransactionsPage>();

        builder.Services.AddTransient<ActivityPage>();
        builder.Services.AddTransient<AccountPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static Uri GetApiBaseAddress()
    {
        var configuredValue =
            typeof(MauiProgram).Assembly
                .GetCustomAttributes<
                    AssemblyMetadataAttribute>()
                .FirstOrDefault(
                    attribute =>
                        string.Equals(
                            attribute.Key,
                            ApiBaseUrlMetadataName,
                            StringComparison.Ordinal))?
                .Value;

#if DEBUG
        const bool allowLocalDevelopmentEndpoint =
            true;
#else
        const bool allowLocalDevelopmentEndpoint =
            false;
#endif

        return BillWatchApiEndpoint.Parse(
            configuredValue,
            allowLocalDevelopmentEndpoint);
    }
}
