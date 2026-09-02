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
    public void TryClassify_GasAndElectricity_ReturnsUtility()
    {
        var result =
            _classifier.TryClassify(
                "RENT_AND_UTILITIES",
                "RENT_AND_UTILITIES_GAS_AND_ELECTRICITY",
                out var category);

        Assert.True(result);
        Assert.Equal(
            BillCategory.Utility,
            category);
    }

    [Theory]
    [InlineData("RENT_AND_UTILITIES_WATER")]
    [InlineData("RENT_AND_UTILITIES_SEWAGE_AND_WASTE_MANAGEMENT")]
    [InlineData("RENT_AND_UTILITIES_OTHER_UTILITIES")]
    public void TryClassify_OtherUtilities_ReturnsUtility(
        string detailedCategory)
    {
        var result =
            _classifier.TryClassify(
                "RENT_AND_UTILITIES",
                detailedCategory,
                out var category);

        Assert.True(result);
        Assert.Equal(
            BillCategory.Utility,
            category);
    }

    [Fact]
    public void TryClassify_Rent_ReturnsOther()
    {
        var result =
            _classifier.TryClassify(
                "RENT_AND_UTILITIES",
                "RENT_AND_UTILITIES_RENT",
                out var category);

        Assert.True(result);
        Assert.Equal(
            BillCategory.Other,
            category);
    }

    [Theory]
    [InlineData("LOAN_PAYMENTS_BNPL")]
    [InlineData("LOAN_PAYMENTS_PERSONAL_LOAN_PAYMENT")]
    [InlineData("LOAN_PAYMENTS_CREDIT_CARD_PAYMENT")]
    [InlineData("LOAN_PAYMENTS_AUTO_LOAN_PAYMENT")]
    [InlineData("LOAN_PAYMENTS_MORTGAGE_PAYMENT")]
    [InlineData("LOAN_PAYMENTS_STUDENT_LOAN_PAYMENT")]
    public void TryClassify_TrueLoanRepayment_ReturnsOther(
        string detailedCategory)
    {
        var result =
            _classifier.TryClassify(
                "LOAN_PAYMENTS",
                detailedCategory,
                out var category);

        Assert.True(result);
        Assert.Equal(
            BillCategory.Other,
            category);
    }

    [Theory]
    [InlineData("LOAN_PAYMENTS_CASH_ADVANCES")]
    [InlineData("LOAN_PAYMENTS_EWA")]
    public void TryClassify_NonBillBorrowingEvent_IsRejected(
        string detailedCategory)
    {
        var result =
            _classifier.TryClassify(
                "LOAN_PAYMENTS",
                detailedCategory,
                out var category);

        Assert.False(result);
        Assert.Equal(
            BillCategory.Unknown,
            category);
    }

    [Theory]
    [InlineData("ENTERTAINMENT_TV_AND_MOVIES")]
    [InlineData("ENTERTAINMENT_MUSIC_AND_AUDIO")]
    public void TryClassify_StreamingMedia_ReturnsOther(
        string detailedCategory)
    {
        var result =
            _classifier.TryClassify(
                "ENTERTAINMENT",
                detailedCategory,
                out var category);

        Assert.True(result);
        Assert.Equal(
            BillCategory.Other,
            category);
    }

    [Theory]
    [InlineData("GENERAL_SERVICES_INSURANCE")]
    [InlineData("GENERAL_SERVICES_STORAGE")]
    public void TryClassify_RecurringGeneralService_ReturnsOther(
        string detailedCategory)
    {
        var result =
            _classifier.TryClassify(
                "GENERAL_SERVICES",
                detailedCategory,
                out var category);

        Assert.True(result);
        Assert.Equal(
            BillCategory.Other,
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
    public void TryClassify_HomeImprovement_IsRejected()
    {
        var result =
            _classifier.TryClassify(
                "HOME_IMPROVEMENT",
                "HOME_IMPROVEMENT_HARDWARE",
                out var category);

        Assert.False(result);
        Assert.Equal(
            BillCategory.Unknown,
            category);
    }

    [Fact]
    public void TryClassify_AccountTransfer_IsRejected()
    {
        var result =
            _classifier.TryClassify(
                "TRANSFER_OUT",
                "TRANSFER_OUT_ACCOUNT_TRANSFER",
                out var category);

        Assert.False(result);
        Assert.Equal(
            BillCategory.Unknown,
            category);
    }

    [Fact]
    public void TryClassify_BroadEntertainment_IsRejected()
    {
        var result =
            _classifier.TryClassify(
                "ENTERTAINMENT",
                "ENTERTAINMENT_OTHER_ENTERTAINMENT",
                out var category);

        Assert.False(result);
        Assert.Equal(
            BillCategory.Unknown,
            category);
    }

    [Fact]
    public void TryClassify_AutomotiveService_IsRejected()
    {
        var result =
            _classifier.TryClassify(
                "GENERAL_SERVICES",
                "GENERAL_SERVICES_AUTOMOTIVE",
                out var category);

        Assert.False(result);
        Assert.Equal(
            BillCategory.Unknown,
            category);
    }
}