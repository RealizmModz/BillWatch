using BillWatch.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace BillWatch.ViewModels;

public sealed class MainPageViewModel : INotifyPropertyChanged
{
    private readonly BillStreamService
        _billStreamService;

    private readonly BillAlertService
        _billAlertService;

    public MainPageViewModel(
        BillStreamService billStreamService,
        BillAlertService billAlertService)
    {
        _billStreamService =
            billStreamService;

        _billAlertService =
            billAlertService;

        OpenActivityCommand =
            new Command(
                async () =>
                    await Shell.Current.GoToAsync(
                        "//Activity"));

        OpenBillsCommand =
            new Command(
                async () =>
                    await Shell.Current.GoToAsync(
                        "//Bills"));

        RefreshCommand =
            new Command(
                async () =>
                    await LoadAsync());
    }

    public int BillsMonitored
    {
        get;
        private set;
    }

    public decimal TotalMonthlyBills
    {
        get;
        private set;
    }

    public decimal TotalAnnualBills
    {
        get;
        private set;
    }

    public int ChangesDetected
    {
        get;
        private set;
    }

    public decimal AddedAnnualCost
    {
        get;
        private set;
    }

    public decimal ReducedAnnualCost
    {
        get;
        private set;
    }

    public int UnreadAlerts
    {
        get;
        private set;
    }

    public int AttentionCount
    {
        get;
        private set;
    }

    public string PrimaryAlertType
    {
        get;
        private set;
    } =
        string.Empty;

    public string PrimaryAlertTitle
    {
        get;
        private set;
    } =
        string.Empty;

    public string PrimaryAlertMessage
    {
        get;
        private set;
    } =
        string.Empty;

    public bool IsLoading
    {
        get;
        private set;
    }

    public string ErrorMessage
    {
        get;
        private set;
    } =
        string.Empty;

    public bool HasError =>
        !string.IsNullOrWhiteSpace(
            ErrorMessage);

    public bool HasContent =>
        !IsLoading &&
        !HasError;

    public bool HasBills =>
        BillsMonitored >
        0;

    public bool HasNoBills =>
        !HasBills;

    public bool HasAttention =>
        AttentionCount >
        0;

    public bool HasNoAttention =>
        !HasAttention;

    public string MonthlyBillsText =>
        $"${TotalMonthlyBills:0.00}";

    public string AnnualBillsText =>
        $"${TotalAnnualBills:0.00} per year monitored";

    public string AnnualImpactHeadline
    {
        get
        {
            if (AddedAnnualCost >
                0m)
            {
                return
                    $"+${AddedAnnualCost:0.00}/year";
            }

            if (ReducedAnnualCost >
                0m)
            {
                return
                    $"${ReducedAnnualCost:0.00}/year less";
            }

            return
                "$0/year";
        }
    }

    public string AnnualImpactCaption
    {
        get
        {
            if (AddedAnnualCost >
                0m)
            {
                return
                    "New annual cost detected";
            }

            if (ReducedAnnualCost >
                0m)
            {
                return
                    "Annual cost reduction detected";
            }

            return
                "No meaningful cost change detected";
        }
    }

    public string MonitoringSummary =>
        BillsMonitored switch
        {
            0 =>
                "No recurring bills monitored yet",

            1 =>
                "1 recurring bill monitored",

            _ =>
                $"{BillsMonitored} recurring bills monitored"
        };

    public string ChangeSummary =>
        ChangesDetected switch
        {
            0 =>
                "No meaningful changes",

            1 =>
                "1 meaningful change",

            _ =>
                $"{ChangesDetected} meaningful changes"
        };

    public string AlertSummary =>
        UnreadAlerts switch
        {
            0 =>
                "No unread alerts",

            1 =>
                "1 unread alert",

            _ =>
                $"{UnreadAlerts} unread alerts"
        };

    public string AttentionHeadline =>
        AttentionCount switch
        {
            0 =>
                "Nothing needs your attention",

            1 =>
                "1 item needs attention",

            _ =>
                $"{AttentionCount} items need attention"
        };

    public string StatusHeadline =>
        HasBills
            ? "Watching"
            : "Ready to watch";

    public string StatusDescription
    {
        get
        {
            if (!HasBills)
            {
                return
                    "Connect a bank or add bill history to start automatic monitoring.";
            }

            if (HasAttention)
            {
                return
                    $"BillWatch is monitoring {BillsMonitored} {(BillsMonitored == 1 ? "bill" : "bills")} and found {AttentionCount} {(AttentionCount == 1 ? "item" : "items")} that need attention.";
            }

            return
                $"BillWatch is monitoring {BillsMonitored} {(BillsMonitored == 1 ? "bill" : "bills")} and nothing currently needs attention.";
        }
    }

    public ICommand OpenActivityCommand
    {
        get;
    }

    public ICommand OpenBillsCommand
    {
        get;
    }

    public ICommand RefreshCommand
    {
        get;
    }

    public async Task LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsLoading)
        {
            return;
        }

        try
        {
            IsLoading =
                true;

            ErrorMessage =
                string.Empty;

            NotifyAll();

            /*
             * These requests are independent, so run them together.
             * Home should feel immediate rather than serially waiting
             * for bill data and then alert data.
             */
            var billStreamsTask =
                _billStreamService
                    .GetBillStreamsAsync(
                        cancellationToken);

            var alertsTask =
                _billAlertService
                    .GetAlertsAsync(
                        includeDismissed:
                            false,

                        unreadOnly:
                            false,

                        take:
                            100,

                        cancellationToken:
                            cancellationToken);

            await Task.WhenAll(
                billStreamsTask,
                alertsTask);

            var billStreams =
                await billStreamsTask;

            var alerts =
                await alertsTask;

            ApplyBillSummary(
                billStreams);

            ApplyAlertSummary(
                alerts);
        }
        catch (SessionExpiredException)
        {
            ErrorMessage =
                "Your BillWatch session expired. Please sign in again.";
        }
        catch (HttpRequestException)
        {
            ErrorMessage =
                "BillWatch couldn't load your dashboard right now.";
        }
        catch (OperationCanceledException)
            when (cancellationToken
                .IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            ErrorMessage =
                "Something went wrong while loading your dashboard.";
        }
        finally
        {
            IsLoading =
                false;

            NotifyAll();
        }
    }

    private void ApplyBillSummary(
        IReadOnlyList<BillStreamResult> billStreams)
    {
        BillsMonitored =
            billStreams.Count;

        TotalMonthlyBills =
            decimal.Round(
                billStreams.Sum(
                    stream =>
                        stream.CurrentAmount),
                2,
                MidpointRounding.AwayFromZero);

        TotalAnnualBills =
            decimal.Round(
                TotalMonthlyBills *
                12m,
                2,
                MidpointRounding.AwayFromZero);

        /*
         * Keep the same deterministic "meaningful change" threshold
         * used by the Bills experience.
         *
         * Home performs arithmetic only; it does not invent causes.
         */
        var meaningfulChanges =
            billStreams
                .Where(
                    stream =>
                        stream.PreviousAverage >
                        0m)
                .Select(
                    stream =>
                    {
                        var monthlyChange =
                            decimal.Round(
                                stream.CurrentAmount -
                                stream.PreviousAverage,
                                2,
                                MidpointRounding.AwayFromZero);

                        return new DashboardBillChange(
                            MonthlyChange:
                                monthlyChange,

                            PreviousAmount:
                                stream.PreviousAverage);
                    })
                .Where(
                    change =>
                        Math.Abs(
                            change.MonthlyChange) >=
                            5m &&
                        Math.Abs(
                            change.MonthlyChange /
                            change.PreviousAmount) >=
                            0.10m)
                .ToList();

        ChangesDetected =
            meaningfulChanges.Count;

        AddedAnnualCost =
            decimal.Round(
                meaningfulChanges
                    .Where(
                        change =>
                            change.MonthlyChange >
                            0m)
                    .Sum(
                        change =>
                            change.MonthlyChange *
                            12m),
                2,
                MidpointRounding.AwayFromZero);

        ReducedAnnualCost =
            decimal.Round(
                meaningfulChanges
                    .Where(
                        change =>
                            change.MonthlyChange <
                            0m)
                    .Sum(
                        change =>
                            Math.Abs(
                                change.MonthlyChange) *
                            12m),
                2,
                MidpointRounding.AwayFromZero);
    }

    private void ApplyAlertSummary(
        IReadOnlyList<BillAlertResult> alerts)
    {
        UnreadAlerts =
            alerts.Count(
                alert =>
                    !alert.IsRead);

        var attentionAlerts =
            alerts
                .Where(
                    alert =>
                        IsAttentionSeverity(
                            alert.Severity))
                .OrderBy(
                    alert =>
                        alert.IsRead)
                .ThenByDescending(
                    alert =>
                        IsCritical(
                            alert.Severity))
                .ThenByDescending(
                    alert =>
                        alert.CreatedAtUtc)
                .ToList();

        AttentionCount =
            attentionAlerts.Count;

        var primaryAlert =
            attentionAlerts
                .FirstOrDefault();

        if (primaryAlert is
            null)
        {
            PrimaryAlertType =
                string.Empty;

            PrimaryAlertTitle =
                string.Empty;

            PrimaryAlertMessage =
                string.Empty;

            return;
        }

        PrimaryAlertType =
            FormatAlertType(
                primaryAlert.AlertType);

        PrimaryAlertTitle =
            primaryAlert.Title;

        PrimaryAlertMessage =
            primaryAlert.Message;
    }

    private static bool IsAttentionSeverity(
        string severity)
    {
        return
            string.Equals(
                severity,
                "Warning",
                StringComparison.Ordinal) ||
            string.Equals(
                severity,
                "Critical",
                StringComparison.Ordinal);
    }

    private static bool IsCritical(
        string severity)
    {
        return string.Equals(
            severity,
            "Critical",
            StringComparison.Ordinal);
    }

    private static string FormatAlertType(
        string alertType)
    {
        return alertType switch
        {
            "BillIncrease" =>
                "BILL INCREASE",

            "BillDecrease" =>
                "BILL DECREASE",

            "NewFee" =>
                "NEW FEE",

            "RemovedDiscount" =>
                "DISCOUNT REMOVED",

            "PaymentDue" =>
                "PAYMENT DUE",

            "ConnectionIssue" =>
                "CONNECTION ISSUE",

            _ =>
                "NEEDS ATTENTION"
        };
    }

    private void NotifyAll()
    {
        OnPropertyChanged(
            string.Empty);
    }

    public event PropertyChangedEventHandler?
        PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName]
        string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }

    private sealed record DashboardBillChange(
        decimal MonthlyChange,
        decimal PreviousAmount);
}