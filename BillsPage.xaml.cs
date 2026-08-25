using BillWatch.ViewModels;

namespace BillWatch;

public partial class BillsPage : ContentPage
{
    public BillsPage()
    {
        InitializeComponent();

        BindingContext = new BillsPageViewModel();
    }
}