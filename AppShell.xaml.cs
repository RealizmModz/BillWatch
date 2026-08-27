using BillWatch.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch;

public partial class AppShell : Shell
{
    private AuthenticationService?
        _authenticationService;

    private bool _startupChecked;

    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(
            nameof(ConnectBankPage),
            typeof(ConnectBankPage));

        Loaded +=
            OnShellLoaded;
    }

    private async void OnShellLoaded(
        object? sender,
        EventArgs e)
    {
        if (_startupChecked)
        {
            return;
        }

        _startupChecked =
            true;

        var services =
            Handler?
                .MauiContext?
                .Services;

        if (services is null)
        {
            await GoToAsync(
                "//Login");

            return;
        }

        _authenticationService =
            services.GetRequiredService<
                AuthenticationService>();

        _authenticationService
            .SessionExpired +=
                OnSessionExpired;

        var isAuthenticated =
            await _authenticationService
                .IsAuthenticatedAsync();

        if (isAuthenticated)
        {
            await GoToAsync(
                "//Home");

            return;
        }

        await GoToAsync(
            "//Login");
    }

    private void OnSessionExpired(
        object? sender,
        EventArgs e)
    {
        Dispatcher.Dispatch(
            () =>
            {
                _ =
                    NavigateToLoginAsync();
            });
    }

    private async Task NavigateToLoginAsync()
    {
        await GoToAsync(
            "//Login");
    }
}