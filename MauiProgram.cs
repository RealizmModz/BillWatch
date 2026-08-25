using BillWatch.Services;
using BillWatch.ViewModels;
using Microsoft.Extensions.Logging;

namespace BillWatch;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
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
                BaseAddress = new Uri(
                    "https://localhost:7243")
            });

        builder.Services.AddSingleton<BillWatchApiClient>();
        builder.Services.AddSingleton<AuthSession>();
        builder.Services.AddSingleton<AuthenticationService>();
        builder.Services.AddSingleton<BillStreamService>();

        builder.Services.AddTransient<LoginPageViewModel>();
        builder.Services.AddTransient<LoginPage>();

        builder.Services.AddTransient<BillsPageViewModel>();
        builder.Services.AddTransient<BillsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}