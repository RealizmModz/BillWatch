using BillWatch.Core.Models;
using BillWatch.Core.Services;
using BillWatch.Services;

namespace BillWatch.ViewModels;

public sealed class MainPageViewModel
{
    public string ProviderName { get; }

    public decimal PreviousAmount { get; }

    public decimal CurrentAmount { get; }

    public decimal MonthlyChange { get; }

    public decimal AnnualChange { get; }

    public string Summary { get; }

    public string Confidence { get; }

    public IReadOnlyList<BillExplanationItem> Changes { get; }

    public decimal BankTransactionAmount { get; }

    public bool BankTransactionMatches { get; }

    public string BankTransactionStatus { get; }

    public int RecurringBillsFound { get; }

    public IReadOnlyList<RecurringBillDetectionResult> RecurringBills { get; }

    public int AlertsFound { get; }

    public IReadOnlyList<BillAlert> Alerts { get; }

    public int BillStreamsFound { get; }

    public IReadOnlyList<BillStream> BillStreams { get; }

    public int BillsMonitored { get; }

    public decimal TotalMonthlyBills { get; }

    public decimal TotalAnnualBills { get; }

    public int ChangesDetected { get; }

    public decimal AddedAnnualCost { get; }

    public decimal ReducedAnnualCost { get; }

    public MainPageViewModel()
    {
        var developmentDataService =
            new DevelopmentDataService();

        var transactionHistory =
            developmentDataService.GetTransactions();

        var billStatements =
            developmentDataService.GetStatements();

        var recurringBillDetectionService =
            new RecurringBillDetectionService();

        RecurringBills =
            recurringBillDetectionService.Detect(
                transactionHistory);

        RecurringBillsFound =
            RecurringBills.Count;

        var alertService =
            new BillAlertService();

        Alerts =
            alertService.CreateAlerts(
                RecurringBills);

        AlertsFound =
            Alerts.Count;

        var billStreamDiscoveryService =
            new BillStreamDiscoveryService();

        BillStreams =
            billStreamDiscoveryService.Discover(
                transactionHistory,
                billStatements);

        BillStreamsFound =
            BillStreams.Count;

        var dashboardSummaryService =
            new DashboardSummaryService();

        var dashboardSummary =
            dashboardSummaryService.CreateSummary(
                BillStreams,
                Alerts);

        BillsMonitored =
            dashboardSummary.BillsMonitored;

        TotalMonthlyBills =
            dashboardSummary.MonthlyBills;

        TotalAnnualBills =
            dashboardSummary.AnnualBills;

        ChangesDetected =
            dashboardSummary.ChangesDetected;

        AddedAnnualCost =
            dashboardSummary.AddedAnnualCost;

        ReducedAnnualCost =
            dashboardSummary.ReducedAnnualCost;

        var midcoStream =
            BillStreams.FirstOrDefault(stream =>
                string.Equals(
                    stream.ProviderName,
                    "Midco",
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "The development Midco Bill Stream could not be found.");

        var billStreamAnalysisService =
            new BillStreamAnalysisService();

        var analysisResult =
            billStreamAnalysisService.Analyze(
                midcoStream)
            ?? throw new InvalidOperationException(
                "The development Midco Bill Stream does not contain enough statement history to analyze.");

        ProviderName =
            analysisResult.Explanation.ProviderName;

        PreviousAmount =
            analysisResult.PreviousStatement
                .TotalAmount
                .Amount;

        CurrentAmount =
            analysisResult.CurrentStatement
                .TotalAmount
                .Amount;

        MonthlyChange =
            analysisResult.Explanation.MonthlyChange;

        AnnualChange =
            analysisResult.Explanation.AnnualChange;

        Summary =
            analysisResult.Explanation.Summary;

        Confidence =
            analysisResult.Explanation
                .Confidence
                .ToString();

        Changes =
            analysisResult.Explanation.Changes;

        BankTransactionAmount =
            analysisResult.Reconciliation?.TransactionAmount
            ?? 0m;

        BankTransactionMatches =
            analysisResult.Reconciliation?.IsConfirmedMatch
            ?? false;

        BankTransactionStatus =
            BankTransactionMatches
                ? "Confirmed"
                : "Needs review";
    }
}