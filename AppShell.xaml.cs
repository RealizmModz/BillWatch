namespace BillWatch;

public partial class AppShell : Shell
{
    private static bool _routesRegistered;

    public AppShell()
    {
        InitializeComponent();
        RegisterRoutes();
    }

    private static void RegisterRoutes()
    {
        if (_routesRegistered)
        {
            return;
        }

        Routing.RegisterRoute(nameof(ConnectBankPage), typeof(ConnectBankPage));
        Routing.RegisterRoute(nameof(TransactionsPage), typeof(TransactionsPage));
        _routesRegistered = true;
    }
}
