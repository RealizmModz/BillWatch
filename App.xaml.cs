using BillWatch.Services;

namespace BillWatch;

public partial class App : Application
{
    private readonly AuthenticationService
        _authenticationService;

    private AppShell?
        _appShell;

    public App(
        AuthenticationService authenticationService)
    {
        InitializeComponent();

        _authenticationService =
            authenticationService;

        _authenticationService.SessionExpired +=
            OnSessionExpired;
    }

    protected override Window CreateWindow(
        IActivationState? activationState)
    {
        _appShell =
            new AppShell();

        return new Window(
            _appShell);
    }

    private void OnSessionExpired(
        object? sender,
        EventArgs e)
    {
        var shell =
            _appShell;

        if (shell is null)
        {
            return;
        }

        _ =
            MainThread.InvokeOnMainThreadAsync(
                () =>
                    shell.GoToAsync(
                        "//Login"));
    }
}