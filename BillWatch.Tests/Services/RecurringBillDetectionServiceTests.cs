using BillWatch.Core.Models;
using BillWatch.Core.Services;
using Xunit;

namespace BillWatch.Tests.Services;

public sealed class RecurringBillDetectionServiceTests
{
    private readonly RecurringBillDetectionService
        _service = new();

    [Fact]
    public void Detect_MonthEndDateMovement_RemainsMonthly()
    {
        var transactions = new[]
        {
            Transaction("Example Subscription", 2026, 1, 31, 19.99m),
            Transaction("Example Subscription", 2026, 2, 28, 19.99m),
            Transaction("Example Subscription", 2026, 4, 2, 19.99m)
        };

        var result =
            Assert.Single(
                _service.Detect(
                    transactions));

        Assert.Equal(
            RecurringBillFrequency.Monthly,
            result.Frequency);

        Assert.Equal(
            3,
            result.TransactionCount);
    }

    [Fact]
    public void Detect_OneMissingMonthlyObservation_RemainsMonthly()
    {
        var transactions = new[]
        {
            Transaction("Example Subscription", 2026, 1, 5, 12.50m),
            Transaction("Example Subscription", 2026, 2, 5, 12.50m),
            Transaction("Example Subscription", 2026, 4, 5, 12.50m)
        };

        var result =
            Assert.Single(
                _service.Detect(
                    transactions));

        Assert.Equal(
            RecurringBillFrequency.Monthly,
            result.Frequency);
    }

    [Fact]
    public void Detect_QuarterlyPattern_IsNotMisclassifiedAsMonthly()
    {
        var transactions = new[]
        {
            Transaction("Quarterly Service", 2026, 1, 5, 40m),
            Transaction("Quarterly Service", 2026, 4, 5, 40m),
            Transaction("Quarterly Service", 2026, 7, 5, 40m)
        };

        Assert.Empty(
            _service.Detect(
                transactions));
    }

    [Fact]
    public void Detect_IrregularRepeat_IsNotPromoted()
    {
        var transactions = new[]
        {
            Transaction("Irregular Merchant", 2026, 1, 5, 25m),
            Transaction("Irregular Merchant", 2026, 1, 20, 25m),
            Transaction("Irregular Merchant", 2026, 2, 15, 25m)
        };

        Assert.Empty(
            _service.Detect(
                transactions));
    }

    private static BankTransaction Transaction(
        string merchant,
        int year,
        int month,
        int day,
        decimal amount)
    {
        return new BankTransaction(
            merchant,
            new DateOnly(
                year,
                month,
                day),
            amount);
    }
}
