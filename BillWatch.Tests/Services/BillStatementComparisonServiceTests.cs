using BillWatch.Core.Models;
using BillWatch.Core.Services;
using Xunit;

namespace BillWatch.Tests.Services;

public sealed class BillStatementComparisonServiceTests
{
    [Fact]
    public void Compare_MidcoIncrease_ExplainsEntireTwentyFiveDollarChange()
    {
        var previousStatement = new BillStatement(
            "Midco",
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 30),
            new BillAmount(79.99m),
            [
                new BillLineItem("Internet service", 99.99m),
                new BillLineItem("Promotional discount", -20.00m)
            ]);

        var currentStatement = new BillStatement(
            "Midco",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            new BillAmount(104.99m),
            [
                new BillLineItem("Internet service", 99.99m),
                new BillLineItem("Network fee", 5.00m)
            ]);

        var service = new BillStatementComparisonService();

        var result = service.Compare(
            previousStatement,
            currentStatement);

        Assert.Equal("Midco", result.ProviderName);

        Assert.Equal(
            25.00m,
            result.TotalComparison.MonthlyChange);

        Assert.Equal(
            300.00m,
            result.TotalComparison.AnnualChange);

        var promotionChange = Assert.Single(
            result.LineItemChanges,
            change => change.Name == "Promotional discount");

        Assert.Equal(
            20.00m,
            promotionChange.Difference);

        Assert.Equal(
            BillLineItemChangeType.Removed,
            promotionChange.ChangeType);

        var feeChange = Assert.Single(
            result.LineItemChanges,
            change => change.Name == "Network fee");

        Assert.Equal(
            5.00m,
            feeChange.Difference);

        Assert.Equal(
            BillLineItemChangeType.Added,
            feeChange.ChangeType);

        var serviceChange = Assert.Single(
            result.LineItemChanges,
            change => change.Name == "Internet service");

        Assert.Equal(
            0.00m,
            serviceChange.Difference);

        Assert.Equal(
            BillLineItemChangeType.Unchanged,
            serviceChange.ChangeType);
    }

    [Fact]
    public void Compare_DifferentProviders_IsRejected()
    {
        var previousStatement = new BillStatement(
            "Midco",
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 30),
            new BillAmount(79.99m),
            []);

        var currentStatement = new BillStatement(
            "Verizon",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            new BillAmount(104.99m),
            []);

        var service = new BillStatementComparisonService();

        Assert.Throws<ArgumentException>(
            () => service.Compare(
                previousStatement,
                currentStatement));
    }
}