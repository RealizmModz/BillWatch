using BillWatch.ViewModels;

namespace BillWatch;

public partial class LoginPage : ContentPage
{
    private readonly LoginPageViewModel _viewModel;

    public LoginPage(
        LoginPageViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    private async void OnSignInClicked(
        object? sender,
        EventArgs e)
    {
        var success = await _viewModel.LoginAsync();

        if (!success)
        {
            return;
        }

        await Shell.Current.GoToAsync("//Home");
    }
}