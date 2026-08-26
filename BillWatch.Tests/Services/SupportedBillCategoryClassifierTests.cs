using BillWatch.Core.Models;
using BillWatch.Core.Services;
using Xunit;

namespace BillWatch.Tests.Services;

public sealed class SupportedBillCategoryClassifierTests
{
    private readonly SupportedBillCategoryClassifier
        _classifier = new();

    [Fact]
    public void TryClassify_InternetAndCable_ReturnsInternet()
    {
        var result =
            _classifier.TryClassify(
                "RENT_AND_UTILITIES",
                "RENT_AND_UTILITIES_INTERNET_AND_CABLE",
                out var category);

        Assert.True(result);
        Assert.Equal(
            BillCategory.Internet,
            category);
    }

    [Fact]
    public void TryClassify_Telephone_ReturnsMobilePhone()
    {
        var result =
            _classifier.TryClassify(
                "RENT_AND_UTILITIES",
                "RENT_AND_UTILITIES_TELEPHONE",
                out var category);

        Assert.True(result);
        Assert.Equal(
            BillCategory.MobilePhone,
            category);
    }

    [Fact]
    public void TryClassify_IsCaseInsensitive()
    {
        var result =
            _classifier.TryClassify(
                "rent_and_utilities",
                "rent_and_utilities_internet_and_cable",
                out var category);

        Assert.True(result);
        Assert.Equal(
            BillCategory.Internet,
            category);
    }

    [Fact]
    public void TryClassify_Restaurant_IsRejected()
    {
        var result =
            _classifier.TryClassify(
                "FOOD_AND_DRINK",
                "FOOD_AND_DRINK_RESTAURANT",
                out var category);

        Assert.False(result);
        Assert.Equal(
            BillCategory.Unknown,
            category);
    }

    [Fact]
    public void TryClassify_Rent_IsRejected()
    {
        var result =
            _classifier.TryClassify(
                "RENT_AND_UTILITIES",
                "RENT_AND_UTILITIES_RENT",
                out var category);

        Assert.False(result);
        Assert.Equal(
            BillCategory.Unknown,
            category);
    }
}