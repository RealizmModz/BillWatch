namespace BillWatch.Core.Services;

public sealed class BillAlertService
{
    public IReadOnlyList<BillAlert> CreateAlerts(
        IEnumerable<RecurringBillDetectionResult> recurringBills)
    {
        ArgumentNullException.ThrowIfNull(recurringBills);

        return recurringBills
            .Where(bill => bill.HasMeaningfulChange)
            .Select(CreateAlert)
            .OrderByDescending(alert => Math.Abs(alert.MonthlyImpact))
            .ToList()
            .AsReadOnly();
    }

    private static BillAlert CreateAlert(
        RecurringBillDetectionResult bill)
    {
        var alertType = bill.LatestDifference >= 0m
            ? BillAlertType.Increase
            : BillAlertType.Decrease;

        decimal annualImpact = decimal.Round(
            bill.LatestDifference * 12m,
            2,
            MidpointRounding.AwayFromZero);

        return new BillAlert(
            MerchantName: bill.MerchantName,
            AlertType: alertType,
            PreviousAverage: bill.AverageAmount,
            CurrentAmount: bill.LatestAmount,
            MonthlyImpact: bill.LatestDifference,
            AnnualImpact: annualImpact,
            PercentageChange: bill.LatestPercentageChange,
            DetectedDate: bill.LatestChargeDate);
    }
}

public enum BillAlertType
{
    Increase,
    Decrease
}

public sealed record BillAlert(
    string MerchantName,
    BillAlertType AlertType,
    decimal PreviousAverage,
    decimal CurrentAmount,
    decimal MonthlyImpact,
    decimal AnnualImpact,
    decimal PercentageChange,
    DateOnly DetectedDate);