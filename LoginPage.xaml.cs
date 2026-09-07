using BillWatch.Core.Legal;
using BillWatch.ViewModels;
using Microsoft.Maui.ApplicationModel;

namespace BillWatch;

public partial class LoginPage : ContentPage
{
    private readonly LoginPageViewModel _viewModel;
    private CancellationTokenSource? _resumeCancellation;
    private bool _resumeAttempted;

    public LoginPage(LoginPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_resumeAttempted) return;
        _resumeAttempted = true;

        _resumeCancellation?.Cancel();
        _resumeCancellation?.Dispose();
        _resumeCancellation = new CancellationTokenSource();

        try
        {
            if (await _viewModel.TryResumeSessionAsync(_resumeCancellation.Token) &&
                !_resumeCancellation.IsCancellationRequested)
            {
                await Shell.Current.GoToAsync("//Home");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    protected override void OnDisappearing()
    {
        _resumeCancellation?.Cancel();
        base.OnDisappearing();
    }

    private async void OnPrimaryActionClicked(object? sender, EventArgs e)
    {
        var destination = await _viewModel.AuthenticateAsync();

        if (destination == LoginPageDestination.Home)
        {
            await Shell.Current.GoToAsync("//Home");
        }
        else if (destination == LoginPageDestination.ConnectBank)
        {
            await Shell.Current.GoToAsync("//Connect");
        }
    }

    private void OnToggleModeClicked(object? sender, EventArgs e)
    {
        _viewModel.ToggleMode();
    }

    private async void OnTermsClicked(object? sender, EventArgs e)
    {
        await Browser.Default.OpenAsync(
            BillWatchLegalDocuments.PublicTermsUrl,
            BrowserLaunchMode.SystemPreferred);
    }

    private async void OnPrivacyClicked(object? sender, EventArgs e)
    {
        await Browser.Default.OpenAsync(
            BillWatchLegalDocuments.PublicPrivacyUrl,
            BrowserLaunchMode.SystemPreferred);
    }
}
