using BillWatch.ViewModels;

namespace BillWatch;

public partial class BillsPage : ContentPage
{
    private readonly BillsPageViewModel _viewModel;

    public BillsPage(
        BillsPageViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.LoadAsync();
    }
}