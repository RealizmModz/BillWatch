using BillWatch.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace BillWatch.ViewModels;

public sealed class ConnectBankPageViewModel :
    INotifyPropertyChanged
{
    private readonly PlaidConnectionService
        _plaidConnectionService;

    private bool _isBusy;

    private CancellationTokenSource?
        _connectionWaitCancellation;

    private string _statusMessage =
        "Securely connect your bank to start monitoring bills.";

    private string _connectedInstitution =
        string.Empty;

    public ConnectBankPageViewModel(
        PlaidConnectionService plaidConnectionService)
    {
        _plaidConnectionService =
            plaidConnectionService;

        ConnectBankCommand =
            new Command(
                async () =>
                    await ConnectBankAsync(),
                () => !IsBusy);

        RefreshConnectionsCommand =
            new Command(
                async () =>
                    await LoadConnectionsAsync(),
                () => !IsBusy);
    }

    public event PropertyChangedEventHandler?
        PropertyChanged;

    public ObservableCollection<BankConnectionItemViewModel>
        Connections
    { get; } = [];

    public ICommand ConnectBankCommand { get; }

    public ICommand RefreshConnectionsCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;

        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy =
                value;

            OnPropertyChanged();
            OnPropertyChanged(
                nameof(HasNoConnections));

            if (ConnectBankCommand
                is Command connectCommand)
            {
                connectCommand
                    .ChangeCanExecute();
            }

            if (RefreshConnectionsCommand
                is Command refreshCommand)
            {
                refreshCommand
                    .ChangeCanExecute();
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

            _statusMessage =
                value;

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

            _connectedInstitution =
                value;

            OnPropertyChanged();

            OnPropertyChanged(
                nameof(
                    HasConnectedInstitution));
        }
    }

    public bool HasConnectedInstitution =>
        !string.IsNullOrWhiteSpace(
            ConnectedInstitution);

    public bool HasConnections =>
        Connections.Count > 0;

    public bool HasNoConnections =>
        Connections.Count == 0 &&
        !IsBusy;

    public async Task LoadConnectionsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy =
                true;

            await RefreshConnectionsCoreAsync(
                updateStatusMessage: true);
        }
        catch
        {
            StatusMessage =
                "BillWatch could not load your bank connections.";
        }
        finally
        {
            IsBusy =
                false;
        }
    }

    public async Task ConnectBankAsync(
        BankConnectionItemViewModel? connection = null)
    {
        if (IsBusy)
        {
            return;
        }

        CancellationTokenSource?
            connectionWaitCancellation =
                null;

        try
        {
            IsBusy =
                true;

            ConnectedInstitution =
                string.Empty;

            StatusMessage =
                connection is null
                    ? "Preparing secure bank connection..."
                    : $"Preparing to reconnect {connection.InstitutionName}...";

            var session =
                connection is null
                    ? await _plaidConnectionService
                        .CreateLinkSessionAsync()
                    : await _plaidConnectionService
                        .CreateUpdateLinkSessionAsync(
                            connection.Id);

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

            connectionWaitCancellation =
                new CancellationTokenSource();

            _connectionWaitCancellation =
                connectionWaitCancellation;

            StatusMessage =
                "Complete the connection in your browser. When you are done — or if you close the Plaid tab — return to BillWatch and click anywhere in this window. BillWatch will refresh or stop waiting.";

            var deadline =
                DateTimeOffset.UtcNow
                    .AddMinutes(10);

            while (DateTimeOffset.UtcNow <
                   deadline)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(2),
                    connectionWaitCancellation.Token);

                var result =
                    await _plaidConnectionService
                        .CompleteLinkSessionAsync(
                            session.SessionId,
                            connectionWaitCancellation
                                .Token);

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
                        result.Connection?
                            .InstitutionName
                        ?? "Connected bank";

                    StatusMessage =
                        connection is null
                            ? $"{ConnectedInstitution} is now securely connected to BillWatch."
                            : $"{ConnectedInstitution} was securely reconnected.";

                    await RefreshConnectionsCoreAsync(
                        updateStatusMessage:
                            false);

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
        catch (OperationCanceledException)
            when (connectionWaitCancellation?
                .IsCancellationRequested == true)
        {
            StatusMessage =
                "Bank connection was canceled.";
        }
        catch
        {
            StatusMessage =
                "BillWatch could not connect to your bank. Please try again.";
        }
        finally
        {
            if (ReferenceEquals(
                    _connectionWaitCancellation,
                    connectionWaitCancellation))
            {
                _connectionWaitCancellation =
                    null;
            }

            connectionWaitCancellation?
                .Dispose();

            IsBusy =
                false;
        }
    }

    public void CancelPendingConnection()
    {
        if (_connectionWaitCancellation
            is not
            {
                IsCancellationRequested:
                    false
            } cancellation)
        {
            return;
        }

        cancellation.Cancel();
    }

    public async Task DisconnectAsync(
        BankConnectionItemViewModel connection)
    {
        ArgumentNullException.ThrowIfNull(
            connection);

        if (IsBusy ||
            !connection.CanDisconnect)
        {
            return;
        }

        try
        {
            IsBusy =
                true;

            StatusMessage =
                $"Disconnecting {connection.InstitutionName}...";

            await _plaidConnectionService
                .DisconnectAsync(
                    connection.Id);

            await RefreshConnectionsCoreAsync(
                updateStatusMessage:
                    false);

            StatusMessage =
                $"{connection.InstitutionName} was disconnected.";
        }
        catch
        {
            StatusMessage =
                $"BillWatch could not disconnect {connection.InstitutionName}.";
        }
        finally
        {
            IsBusy =
                false;
        }
    }

    private async Task RefreshConnectionsCoreAsync(
        bool updateStatusMessage)
    {
        var connections =
            await _plaidConnectionService
                .GetConnectionsAsync();

        Connections.Clear();

        foreach (var connection in connections)
        {
            Connections.Add(
                new BankConnectionItemViewModel(
                    connection));
        }

        OnPropertyChanged(
            nameof(HasConnections));

        OnPropertyChanged(
            nameof(HasNoConnections));

        if (!updateStatusMessage)
        {
            return;
        }

        var activeCount =
            Connections.Count(
                connection =>
                    connection.Status ==
                    BankConnectionStatus.Active);

        var attentionCount =
            Connections.Count(
                connection =>
                    connection.Status ==
                    BankConnectionStatus
                        .RequiresAttention);

        if (activeCount > 0)
        {
            StatusMessage =
                activeCount == 1
                    ? "BillWatch is monitoring 1 bank connection."
                    : $"BillWatch is monitoring {activeCount} bank connections.";

            return;
        }

        if (attentionCount > 0)
        {
            StatusMessage =
                "A bank connection needs your attention.";

            return;
        }

        if (Connections.Count > 0)
        {
            StatusMessage =
                "Your saved bank connections are currently disconnected.";

            return;
        }

        StatusMessage =
            "Securely connect your bank to start monitoring bills.";
    }

    private void OnPropertyChanged(
        [CallerMemberName]
        string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }
}

public sealed class BankConnectionItemViewModel
{
    public BankConnectionItemViewModel(
        BankConnectionResult connection)
    {
        Id =
            connection.Id;

        InstitutionName =
            connection.InstitutionName;

        Status =
            connection.Status;

        LastSuccessfulSyncAtUtc =
            connection.LastSuccessfulSyncAtUtc;

        CreatedAtUtc =
            connection.CreatedAtUtc;
    }

    public Guid Id { get; }

    public string InstitutionName { get; }

    public BankConnectionStatus Status { get; }

    public DateTimeOffset?
        LastSuccessfulSyncAtUtc
    { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public bool CanDisconnect =>
        Status !=
        BankConnectionStatus.Disconnected;

    public bool CanReconnect =>
        Status ==
        BankConnectionStatus.RequiresAttention;

    public string StatusText =>
        Status switch
        {
            BankConnectionStatus.Active =>
                "Connected",

            BankConnectionStatus.RequiresAttention =>
                "Needs attention",

            BankConnectionStatus.Disconnected =>
                "Disconnected",

            _ =>
                "Unknown"
        };

    public string LastSyncText
    {
        get
        {
            if (LastSuccessfulSyncAtUtc
                is not DateTimeOffset value)
            {
                return "Not synced yet";
            }

            return
                $"Last synced {value.ToLocalTime():MMM d, yyyy 'at' h:mm tt}";
        }
    }
}
