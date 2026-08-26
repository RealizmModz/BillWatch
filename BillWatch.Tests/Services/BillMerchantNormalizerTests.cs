using BillWatch.Core.Services;
using Xunit;

namespace BillWatch.Tests.Services;

public sealed class BillMerchantNormalizerTests
{
    private readonly BillMerchantNormalizer
        _normalizer = new();

    [Theory]
    [InlineData("MIDCO", "MIDCO")]
    [InlineData("Midco", "MIDCO")]
    [InlineData("MIDCO AUTOPAY", "MIDCO")]
    [InlineData("MIDCO PAYMENT", "MIDCO")]
    [InlineData("MIDCO ONLINE PAYMENT", "MIDCO")]
    [InlineData("MIDCO*123456", "MIDCO")]
    [InlineData("MIDCO - 9876", "MIDCO")]
    public void Normalize_MidcoVariants_ReturnSameMerchant(
        string input,
        string expected)
    {
        var result =
            _normalizer.Normalize(input);

        Assert.Equal(
            expected,
            result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_BlankValue_ReturnsEmpty(
        string? input)
    {
        var result =
            _normalizer.Normalize(input);

        Assert.Equal(
            string.Empty,
            result);
    }

    [Fact]
    public void Normalize_PreservesMeaningfulWords()
    {
        var result =
            _normalizer.Normalize(
                "Black Hills Energy");

        Assert.Equal(
            "BLACK HILLS ENERGY",
            result);
    }

    [Fact]
    public void Normalize_RemovesTransactionNoise()
    {
        var result =
            _normalizer.Normalize(
                "Verizon Wireless ACH AUTOPAY 482913");

        Assert.Equal(
            "VERIZON WIRELESS",
            result);
    }
}