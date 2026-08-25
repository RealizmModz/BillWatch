using BillWatch.Core.Models;
using Xunit;

namespace BillWatch.Tests.Models;

public sealed class BillAmountTests
{
    [Fact]
    public void ValidAmount_IsStoredCorrectly()
    {
        var billAmount = new BillAmount(104.99m);

        Assert.Equal(104.99m, billAmount.Amount);
    }

    [Fact]
    public void Amount_IsRoundedToTwoDecimalPlaces()
    {
        var billAmount = new BillAmount(79.995m);

        Assert.Equal(80.00m, billAmount.Amount);
    }

    [Fact]
    public void NegativeAmount_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BillAmount(-1m));
    }
}