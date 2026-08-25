using BillWatch.Core.Models;
using BillWatch.Core.Services;
using Xunit;

namespace BillWatch.Tests.Services;

public sealed class BillComparisonServiceTests
{
    [Fact]
    public void Compare_WhenBillIncreases_CalculatesMonthlyAndAnnualImpact()
    {
        var service = new BillComparisonService();

        var previousAmount = new BillAmount(79.99m);
        var currentAmount = new BillAmount(104.99m);

        var result = service.Compare(
            previousAmount,
            currentAmount);

        Assert.Equal(79.99m, result.PreviousAmount);
        Assert.Equal(104.99m, result.CurrentAmount);
        Assert.Equal(25.00m, result.MonthlyChange);
        Assert.Equal(300.00m, result.AnnualChange);
        Assert.Equal(31.25m, result.PercentageChange);
    }

    [Fact]
    public void Compare_WhenBillDecreases_ReturnsNegativeImpact()
    {
        var service = new BillComparisonService();

        var previousAmount = new BillAmount(120.00m);
        var currentAmount = new BillAmount(100.00m);

        var result = service.Compare(
            previousAmount,
            currentAmount);

        Assert.Equal(-20.00m, result.MonthlyChange);
        Assert.Equal(-240.00m, result.AnnualChange);
        Assert.Equal(-16.67m, result.PercentageChange);
    }

    [Fact]
    public void Compare_WhenAmountsAreEqual_ReturnsZeroChange()
    {
        var service = new BillComparisonService();

        var previousAmount = new BillAmount(85.50m);
        var currentAmount = new BillAmount(85.50m);

        var result = service.Compare(
            previousAmount,
            currentAmount);

        Assert.Equal(0m, result.MonthlyChange);
        Assert.Equal(0m, result.AnnualChange);
        Assert.Equal(0m, result.PercentageChange);
    }
}