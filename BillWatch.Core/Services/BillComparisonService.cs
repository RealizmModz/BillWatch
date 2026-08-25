using BillWatch.Core.Models;

namespace BillWatch.Core.Services;

public sealed class BillComparisonService
{
    public BillComparisonResult Compare(
        BillAmount previousAmount,
        BillAmount currentAmount)
    {
        ArgumentNullException.ThrowIfNull(previousAmount);
        ArgumentNullException.ThrowIfNull(currentAmount);

        decimal monthlyChange =
            currentAmount.Amount - previousAmount.Amount;

        decimal annualChange =
            monthlyChange * 12m;

        decimal percentageChange =
            previousAmount.Amount == 0m
                ? 0m
                : (monthlyChange / previousAmount.Amount) * 100m;

        return new BillComparisonResult(
            PreviousAmount: previousAmount.Amount,
            CurrentAmount: currentAmount.Amount,
            MonthlyChange: decimal.Round(
                monthlyChange,
                2,
                MidpointRounding.AwayFromZero),
            AnnualChange: decimal.Round(
                annualChange,
                2,
                MidpointRounding.AwayFromZero),
            PercentageChange: decimal.Round(
                percentageChange,
                2,
                MidpointRounding.AwayFromZero));
    }
}

public sealed record BillComparisonResult(
    decimal PreviousAmount,
    decimal CurrentAmount,
    decimal MonthlyChange,
    decimal AnnualChange,
    decimal PercentageChange);