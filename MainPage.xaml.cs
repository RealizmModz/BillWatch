using BillWatch.ViewModels;

namespace BillWatch;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

        BindingContext = new MainPageViewModel();
    }
}