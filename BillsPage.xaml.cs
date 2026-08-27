using BillWatch.Services;
using BillWatch.ViewModels;

namespace BillWatch;

public partial class BillsPage : ContentPage
{
    private readonly BillsPageViewModel
        _viewModel;

    private readonly BillStreamService
        _billStreamService;

    private bool
        _isOpeningBill;

    public BillsPage(
        BillsPageViewModel viewModel,
        BillStreamService billStreamService)
    {
        InitializeComponent();

        _viewModel =
            viewModel;

        _billStreamService =
            billStreamService;

        BindingContext =
            _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.LoadAsync();
    }

    private async void OnBillTapped(
        object? sender,
        TappedEventArgs e)
    {
        if (_isOpeningBill)
        {
            return;
        }

        var bill =
            e.Parameter as BillListItem;

        if (bill is null ||
            bill.Id == Guid.Empty)
        {
            return;
        }

        try
        {
            _isOpeningBill =
                true;

            var detailPage =
                new BillDetailPage(
                    _billStreamService)
                {
                    BillStreamId =
                        bill.Id.ToString()
                };

            await Navigation.PushModalAsync(
                new NavigationPage(
                    detailPage));
        }
        finally
        {
            _isOpeningBill =
                false;
        }
    }
}