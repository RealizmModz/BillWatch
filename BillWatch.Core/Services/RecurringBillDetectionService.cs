using BillWatch.Core.Models;

namespace BillWatch.Core.Services;

public sealed class RecurringBillDetectionService
{
    public IReadOnlyList<RecurringBillDetectionResult> Detect(
        IEnumerable<BankTransaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        var postedTransactions = transactions
            .Where(transaction => !transaction.IsPending)
            .OrderBy(transaction => transaction.PostedDate)
            .ToList();

        var results = new List<RecurringBillDetectionResult>();

        foreach (var merchantGroup in postedTransactions.GroupBy(
                     transaction => transaction.MerchantName,
                     StringComparer.OrdinalIgnoreCase))
        {
            var merchantTransactions = merchantGroup
                .OrderBy(transaction => transaction.PostedDate)
                .ToList();

            if (merchantTransactions.Count < 3)
            {
                continue;
            }

            var intervals = new List<int>();

            for (int i = 1; i < merchantTransactions.Count; i++)
            {
                int daysBetween =
                    merchantTransactions[i].PostedDate.DayNumber -
                    merchantTransactions[i - 1].PostedDate.DayNumber;

                intervals.Add(daysBetween);
            }

            int monthlyIntervals = intervals.Count(days =>
                days >= 25 &&
                days <= 35);

            bool isMonthly =
                monthlyIntervals >= intervals.Count * 0.75m;

            if (!isMonthly)
            {
                continue;
            }

            var latestTransaction =
                merchantTransactions[^1];

            var previousTransactions =
                merchantTransactions
                    .Take(merchantTransactions.Count - 1)
                    .ToList();

            decimal historicalAverage = decimal.Round(
                previousTransactions.Average(
                    transaction => transaction.Amount),
                2,
                MidpointRounding.AwayFromZero);

            decimal latestDifference = decimal.Round(
                latestTransaction.Amount - historicalAverage,
                2,
                MidpointRounding.AwayFromZero);

            decimal latestPercentageChange =
                historicalAverage == 0m
                    ? 0m
                    : decimal.Round(
                        latestDifference /
                        historicalAverage *
                        100m,
                        2,
                        MidpointRounding.AwayFromZero);

            decimal minimumAmount =
                merchantTransactions.Min(
                    transaction => transaction.Amount);

            decimal maximumAmount =
                merchantTransactions.Max(
                    transaction => transaction.Amount);

            bool hasVariableAmount =
                maximumAmount - minimumAmount > 1.00m;

            bool hasMeaningfulChange =
                Math.Abs(latestDifference) >= 5.00m &&
                Math.Abs(latestPercentageChange) >= 10.00m;

            results.Add(
                new RecurringBillDetectionResult(
                    MerchantName: merchantGroup.Key,
                    Frequency: RecurringBillFrequency.Monthly,
                    AverageAmount: historicalAverage,
                    LatestAmount: latestTransaction.Amount,
                    LatestChargeDate: latestTransaction.PostedDate,
                    TransactionCount: merchantTransactions.Count,
                    HasVariableAmount: hasVariableAmount,
                    LatestDifference: latestDifference,
                    LatestPercentageChange: latestPercentageChange,
                    HasMeaningfulChange: hasMeaningfulChange));
        }

        return results
            .OrderByDescending(result => result.LatestAmount)
            .ToList()
            .AsReadOnly();
    }
}

public enum RecurringBillFrequency
{
    Monthly
}

public sealed record RecurringBillDetectionResult(
    string MerchantName,
    RecurringBillFrequency Frequency,
    decimal AverageAmount,
    decimal LatestAmount,
    DateOnly LatestChargeDate,
    int TransactionCount,
    bool HasVariableAmount,
    decimal LatestDifference,
    decimal LatestPercentageChange,
    bool HasMeaningfulChange);