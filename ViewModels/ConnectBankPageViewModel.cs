using BillWatch.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace BillWatch.ViewModels;

public sealed class ConnectBankPageViewModel : INotifyPropertyChanged
{
    private readonly PlaidConnectionService _plaidConnectionService;

    private bool _isBusy;
    private string _statusMessage =
        "Securely connect your bank to start monitoring bills.";

    private string _connectedInstitution = string.Empty;

    public ConnectBankPageViewModel(
        PlaidConnectionService plaidConnectionService)
    {
        _plaidConnectionService =
            plaidConnectionService;

        ConnectBankCommand =
            new Command(
                async () => await ConnectBankAsync(),
                () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand ConnectBankCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;

        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;

            OnPropertyChanged();

            if (ConnectBankCommand is Command command)
            {
                command.ChangeCanExecute();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;

        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public string ConnectedInstitution
    {
        get => _connectedInstitution;

        private set
        {
            if (_connectedInstitution == value)
            {
                return;
            }

            _connectedInstitution = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasConnectedInstitution));
        }
    }

    public bool HasConnectedInstitution =>
        !string.IsNullOrWhiteSpace(
            ConnectedInstitution);

    private async Task ConnectBankAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            ConnectedInstitution =
                string.Empty;

            StatusMessage =
                "Preparing secure bank connection...";

            var session =
                await _plaidConnectionService
                    .CreateLinkSessionAsync();

            StatusMessage =
                "Opening Plaid...";

            var opened =
                await Launcher.Default.OpenAsync(
                    session.HostedLinkUrl);

            if (!opened)
            {
                StatusMessage =
                    "BillWatch could not open the secure bank connection.";

                return;
            }

            StatusMessage =
                "Complete the connection in your browser. BillWatch is waiting securely...";

            var deadline =
                DateTimeOffset.UtcNow.AddMinutes(10);

            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(2));

                var result =
                    await _plaidConnectionService
                        .CompleteLinkSessionAsync(
                            session.SessionId);

                if (string.Equals(
                        result.Status,
                        "Pending",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(
                        result.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase))
                {
                    ConnectedInstitution =
                        result.Connection?.InstitutionName
                        ?? "Connected bank";

                    StatusMessage =
                        $"{ConnectedInstitution} is now securely connected to BillWatch.";

                    return;
                }

                if (string.Equals(
                        result.Status,
                        "Exited",
                        StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage =
                        "Bank connection was canceled.";

                    return;
                }

                if (string.Equals(
                        result.Status,
                        "Expired",
                        StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage =
                        "The secure connection expired. Try connecting again.";

                    return;
                }

                StatusMessage =
                    "The bank connection could not be completed. Please try again.";

                return;
            }

            StatusMessage =
                "BillWatch stopped waiting for the bank connection. You can try again.";
        }
        catch
        {
            StatusMessage =
                "BillWatch could not connect to your bank. Please try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }
}