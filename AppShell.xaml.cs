namespace BillWatch;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(
            nameof(ConnectBankPage),
            typeof(ConnectBankPage));

        Routing.RegisterRoute(
            nameof(TransactionsPage),
            typeof(TransactionsPage));
    }
}