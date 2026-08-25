using BillWatch.Core.Models;
using BillWatch.Core.Services;
using BillWatch.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BillWatch.ViewModels;

public sealed class MainPageViewModel : INotifyPropertyChanged
{
    private readonly BillStreamService _billStreamService;

    public MainPageViewModel(
        BillStreamService billStreamService)
    {
        _billStreamService = billStreamService;
    }

    public string ProviderName { get; private set; } =
        "No bill selected";

    public decimal PreviousAmount { get; private set; }

    public decimal CurrentAmount { get; private set; }

    public decimal MonthlyChange { get; private set; }

    public decimal AnnualChange { get; private set; }

    public string Summary { get; private set; } =
        "Waiting for bill history.";

    public string Confidence { get; private set; } =
        "Unknown";

    public IReadOnlyList<BillExplanationItem> Changes { get; private set; } =
        [];

    public decimal BankTransactionAmount { get; private set; }

    public bool BankTransactionMatches { get; private set; }

    public string BankTransactionStatus { get; private set; } =
        "Waiting for transaction";

    public string BankTransactionDescription { get; private set; } =
        "No linked bank transaction yet.";

    public bool HasBankTransaction { get; private set; }

    public bool HasStatement { get; private set; }

    public string StatementStatus { get; private set; } =
        "Waiting for statement";

    public string StatementDescription { get; private set; } =
        "No provider statement retrieved yet.";

    public bool HasDetailedChange { get; private set; }

    public string DetailedChangeTitle { get; private set; } =
        "Waiting for bill history";

    public int RecurringBillsFound { get; private set; }

    public IReadOnlyList<RecurringBillDetectionResult> RecurringBills { get; private set; } =
        [];

    public int AlertsFound { get; private set; }

    public IReadOnlyList<BillAlert> Alerts { get; private set; } =
        [];

    public int BillStreamsFound { get; private set; }

    public IReadOnlyList<BillStream> BillStreams { get; private set; } =
        [];

    public int BillsMonitored { get; private set; }

    public decimal TotalMonthlyBills { get; private set; }

    public decimal TotalAnnualBills { get; private set; }

    public int ChangesDetected { get; private set; }

    public decimal AddedAnnualCost { get; private set; }

    public decimal ReducedAnnualCost { get; private set; }

    public bool IsLoading { get; private set; }

    public string ErrorMessage { get; private set; } =
        string.Empty;

    public bool HasError =>
        !string.IsNullOrWhiteSpace(ErrorMessage);

    public async Task LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsLoading)
        {
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
            NotifyAll();

            var billStreams =
                await _billStreamService.GetBillStreamsAsync(
                    cancellationToken);

            BillsMonitored = billStreams.Count;

            TotalMonthlyBills = decimal.Round(
                billStreams.Sum(stream => stream.CurrentAmount),
                2,
                MidpointRounding.AwayFromZero);

            TotalAnnualBills = decimal.Round(
                TotalMonthlyBills * 12m,
                2,
                MidpointRounding.AwayFromZero);

            var meaningfulChanges = billStreams
                .Select(stream => new
                {
                    Stream = stream,
                    MonthlyChange =
                        stream.CurrentAmount -
                        stream.PreviousAverage
                })
                .Where(item =>
                    item.Stream.PreviousAverage > 0m &&
                    Math.Abs(item.MonthlyChange) >= 5m &&
                    Math.Abs(
                        item.MonthlyChange /
                        item.Stream.PreviousAverage) >= 0.10m)
                .ToList();

            ChangesDetected =
                meaningfulChanges.Count;

            AddedAnnualCost = decimal.Round(
                meaningfulChanges
                    .Where(item => item.MonthlyChange > 0m)
                    .Sum(item => item.MonthlyChange * 12m),
                2,
                MidpointRounding.AwayFromZero);

            ReducedAnnualCost = decimal.Round(
                meaningfulChanges
                    .Where(item => item.MonthlyChange < 0m)
                    .Sum(item => Math.Abs(item.MonthlyChange) * 12m),
                2,
                MidpointRounding.AwayFromZero);

            var primaryBill =
                billStreams.FirstOrDefault();

            if (primaryBill is null)
            {
                ResetPrimaryBill();
            }
            else
            {
                ProviderName =
                    primaryBill.ProviderName;

                PreviousAmount =
                    primaryBill.PreviousAverage;

                CurrentAmount =
                    primaryBill.CurrentAmount;

                MonthlyChange = decimal.Round(
                    CurrentAmount - PreviousAmount,
                    2,
                    MidpointRounding.AwayFromZero);

                AnnualChange = decimal.Round(
                    MonthlyChange * 12m,
                    2,
                    MidpointRounding.AwayFromZero);

                HasBankTransaction =
                    CurrentAmount > 0m;

                BankTransactionAmount =
                    CurrentAmount;

                BankTransactionStatus =
                    HasBankTransaction
                        ? "Transaction found"
                        : "Waiting for transaction";

                BankTransactionDescription =
                    HasBankTransaction
                        ? $"{ProviderName} charged ${CurrentAmount:F2}"
                        : "No linked bank transaction yet.";

                HasDetailedChange =
                    PreviousAmount > 0m &&
                    Math.Abs(MonthlyChange) >= 5m &&
                    Math.Abs(
                        MonthlyChange /
                        PreviousAmount) >= 0.10m;

                DetailedChangeTitle =
                    HasDetailedChange
                        ? "Bill change detected"
                        : "Waiting for bill history";

                Summary =
                    PreviousAmount > 0m
                        ? "BillWatch has enough transaction history to compare this bill."
                        : "BillWatch is waiting for more transaction history before comparing this bill.";

                // Statements are not connected to the API yet.
                HasStatement = false;
                StatementStatus =
                    "Waiting for statement";

                StatementDescription =
                    "No provider statement retrieved yet.";

                BankTransactionMatches = false;
                Confidence = "Unknown";
                Changes = [];
            }

            RecurringBillsFound = 0;
            RecurringBills = [];

            AlertsFound = 0;
            Alerts = [];

            BillStreamsFound = 0;
            BillStreams = [];
        }
        catch (HttpRequestException)
        {
            ErrorMessage =
                "Unable to load your BillWatch dashboard.";
        }
        catch (Exception)
        {
            ErrorMessage =
                "Something went wrong while loading your dashboard.";
        }
        finally
        {
            IsLoading = false;
            NotifyAll();
        }
    }

    private void ResetPrimaryBill()
    {
        ProviderName = "No bill selected";

        PreviousAmount = 0m;
        CurrentAmount = 0m;
        MonthlyChange = 0m;
        AnnualChange = 0m;

        HasBankTransaction = false;
        BankTransactionAmount = 0m;
        BankTransactionMatches = false;
        BankTransactionStatus = "Waiting for transaction";
        BankTransactionDescription =
            "No linked bank transaction yet.";

        HasStatement = false;
        StatementStatus = "Waiting for statement";
        StatementDescription =
            "No provider statement retrieved yet.";

        HasDetailedChange = false;
        DetailedChangeTitle =
            "Waiting for bill history";

        Summary = "No monitored bills yet.";
        Confidence = "Unknown";
        Changes = [];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyAll()
    {
        OnPropertyChanged(string.Empty);
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}