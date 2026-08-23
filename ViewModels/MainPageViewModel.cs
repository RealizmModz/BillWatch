using BillWatch.Core.Models;
using BillWatch.Core.Services;

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

    public MainPageViewModel()
    {
        var previousStatement = new BillStatement(
            "Midco",
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 30),
            new BillAmount(79.99m),
            [
                new BillLineItem(
                    "Internet service",
                    99.99m),

                new BillLineItem(
                    "Promotional discount",
                    -20.00m)
            ]);

        var currentStatement = new BillStatement(
            "Midco",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            new BillAmount(104.99m),
            [
                new BillLineItem(
                    "Internet service",
                    99.99m),

                new BillLineItem(
                    "Network fee",
                    5.00m)
            ]);

        var bankTransaction = new BankTransaction(
            merchantName: "Midco",
            postedDate: new DateOnly(2026, 5, 18),
            amount: 104.99m,
            isPending: false);

        var analysisService =
            new BillAnalysisService();

        var analysisResult =
            analysisService.Analyze(
                bankTransaction,
                previousStatement,
                currentStatement);

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

        var transactionHistory =
            CreateDevelopmentTransactionHistory();

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

        var billStatements =
            new List<BillStatement>
            {
                previousStatement,
                currentStatement
            };

        var billStreamDiscoveryService =
            new BillStreamDiscoveryService();

        BillStreams =
            billStreamDiscoveryService.Discover(
                transactionHistory,
                billStatements);

        BillStreamsFound =
            BillStreams.Count;
    }

    private static IReadOnlyList<BankTransaction>
        CreateDevelopmentTransactionHistory()
    {
        return
        [
            // Midco
            new("Midco", new DateOnly(2026, 2, 18), 79.99m),
            new("Midco", new DateOnly(2026, 3, 18), 79.99m),
            new("Midco", new DateOnly(2026, 4, 18), 79.99m),
            new("Midco", new DateOnly(2026, 5, 18), 104.99m),

            // Black Hills Energy
            new("Black Hills Energy", new DateOnly(2026, 2, 8), 154.22m),
            new("Black Hills Energy", new DateOnly(2026, 3, 9), 169.41m),
            new("Black Hills Energy", new DateOnly(2026, 4, 8), 165.31m),
            new("Black Hills Energy", new DateOnly(2026, 5, 8), 183.42m),

            // Verizon
            new("Verizon", new DateOnly(2026, 2, 12), 148.22m),
            new("Verizon", new DateOnly(2026, 3, 12), 148.22m),
            new("Verizon", new DateOnly(2026, 4, 11), 148.22m),
            new("Verizon", new DateOnly(2026, 5, 12), 148.22m),

            // Non-recurring purchases
            new("Walmart", new DateOnly(2026, 4, 3), 64.38m),
            new("Walmart", new DateOnly(2026, 4, 17), 83.19m),
            new("Walmart", new DateOnly(2026, 5, 2), 42.76m),
            new("Walmart", new DateOnly(2026, 5, 14), 91.55m),

            new("McDonald's", new DateOnly(2026, 4, 6), 13.42m),
            new("McDonald's", new DateOnly(2026, 4, 20), 18.11m),
            new("McDonald's", new DateOnly(2026, 5, 5), 11.86m),

            new("Holiday Gas", new DateOnly(2026, 4, 2), 38.44m),
            new("Holiday Gas", new DateOnly(2026, 4, 13), 42.17m),
            new("Holiday Gas", new DateOnly(2026, 4, 27), 36.93m),
            new("Holiday Gas", new DateOnly(2026, 5, 10), 40.22m)
        ];
    }
}