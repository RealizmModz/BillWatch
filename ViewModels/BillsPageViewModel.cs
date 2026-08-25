using BillWatch.Core.Models;
using BillWatch.Core.Services;
using BillWatch.Services;

namespace BillWatch.ViewModels;

public sealed class BillsPageViewModel
{
    public int BillsMonitored { get; }

    public decimal MonthlyTotal { get; }

    public IReadOnlyList<BillListItem> Bills { get; }

    public BillsPageViewModel()
    {
        var developmentDataService =
            new DevelopmentDataService();

        var transactions =
            developmentDataService.GetTransactions();

        var statements =
            developmentDataService.GetStatements();

        var detectionService =
            new RecurringBillDetectionService();

        var detectedBills =
            detectionService.Detect(transactions);

        var discoveryService =
            new BillStreamDiscoveryService();

        var billStreams =
            discoveryService.Discover(
                transactions,
                statements);

        var detectionsByProvider =
            detectedBills.ToDictionary(
                bill => bill.MerchantName,
                StringComparer.OrdinalIgnoreCase);

        Bills = billStreams
            .Select(stream =>
                CreateBillListItem(
                    stream,
                    detectionsByProvider))
            .OrderByDescending(bill =>
                bill.HasMeaningfulChange)
            .ThenBy(bill =>
                bill.ProviderName,
                StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

        BillsMonitored =
            Bills.Count;

        MonthlyTotal =
            decimal.Round(
                Bills.Sum(bill => bill.CurrentAmount),
                2,
                MidpointRounding.AwayFromZero);
    }

    private static BillListItem CreateBillListItem(
        BillStream stream,
        IReadOnlyDictionary<
            string,
            RecurringBillDetectionResult> detections)
    {
        detections.TryGetValue(
            stream.ProviderName,
            out var detection);

        decimal currentAmount =
            stream.LatestAmount ?? 0m;

        decimal previousAverage =
            detection?.AverageAmount ?? currentAmount;

        decimal monthlyChange =
            detection?.LatestDifference ?? 0m;

        decimal annualImpact =
            decimal.Round(
                monthlyChange * 12m,
                2,
                MidpointRounding.AwayFromZero);

        bool hasMeaningfulChange =
            detection?.HasMeaningfulChange ?? false;

        return new BillListItem(
            ProviderName: stream.ProviderName,
            Category: FormatCategory(stream.Category),
            CurrentAmount: currentAmount,
            PreviousAverage: previousAverage,
            MonthlyChange: monthlyChange,
            AnnualImpact: annualImpact,
            HasMeaningfulChange: hasMeaningfulChange,
            Status: hasMeaningfulChange
                ? "Needs attention"
                : "Watching");
    }

    private static string FormatCategory(
        BillCategory category)
    {
        return category switch
        {
            BillCategory.Internet =>
                "Internet",

            BillCategory.MobilePhone =>
                "Mobile phone",

            BillCategory.Electricity =>
                "Electricity",

            BillCategory.NaturalGas =>
                "Natural gas",

            BillCategory.Utility =>
                "Utility",

            BillCategory.Other =>
                "Other",

            _ =>
                "Unknown"
        };
    }
}

public sealed record BillListItem(
    string ProviderName,
    string Category,
    decimal CurrentAmount,
    decimal PreviousAverage,
    decimal MonthlyChange,
    decimal AnnualImpact,
    bool HasMeaningfulChange,
    string Status);