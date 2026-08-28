using BillWatch.API.Services.Statements;

namespace BillWatch.Tests.Services;

public sealed class BillStatementValidationServiceTests
{
    private static readonly DateOnly Today =
        new(
            2026,
            8,
            27);

    private readonly BillStatementValidationService
        _service =
            new();

    [Fact]
    public void ValidStatement_HasNoIssues()
    {
        var result =
            _service.Validate(
                new BillStatementValidationInput(
                    TotalAmount:
                        104.99m,

                    PeriodStart:
                        new DateOnly(
                            2026,
                            7,
                            10),

                    PeriodEnd:
                        new DateOnly(
                            2026,
                            8,
                            9),

                    StatementDate:
                        new DateOnly(
                            2026,
                            8,
                            10),

                    DueDate:
                        new DateOnly(
                            2026,
                            8,
                            31),

                    CurrencyCode:
                        "USD"),
                Today);

        Assert.True(
            result.IsValid);

        Assert.False(
            result.RequiresReview);

        Assert.Empty(
            result.Issues);
    }

    [Fact]
    public void MissingRequiredFields_IsRejected()
    {
        var result =
            _service.Validate(
                new BillStatementValidationInput(
                    TotalAmount:
                        null,

                    PeriodStart:
                        null,

                    PeriodEnd:
                        null,

                    StatementDate:
                        null,

                    DueDate:
                        null,

                    CurrencyCode:
                        null),
                Today);

        Assert.False(
            result.IsValid);

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code ==
                "missing_total");

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code ==
                "missing_period_start");

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code ==
                "missing_period_end");

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code ==
                "missing_currency");
    }

    [Fact]
    public void ReversedBillingPeriod_IsRejected()
    {
        var result =
            _service.Validate(
                CreateValidInput() with
                {
                    PeriodStart =
                        new DateOnly(
                            2026,
                            8,
                            10),

                    PeriodEnd =
                        new DateOnly(
                            2026,
                            7,
                            10)
                },
                Today);

        Assert.False(
            result.IsValid);

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code ==
                "reversed_period");
    }

    [Fact]
    public void NegativeTotal_IsRejected()
    {
        var result =
            _service.Validate(
                CreateValidInput() with
                {
                    TotalAmount =
                        -104.99m
                },
                Today);

        Assert.False(
            result.IsValid);

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code ==
                "negative_total");
    }

    [Fact]
    public void InvalidCurrency_IsRejected()
    {
        var result =
            _service.Validate(
                CreateValidInput() with
                {
                    CurrencyCode =
                        "usd"
                },
                Today);

        Assert.False(
            result.IsValid);

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code ==
                "invalid_currency");
    }

    [Fact]
    public void SuspiciousDates_RequireReviewButDoNotInventFailure()
    {
        var result =
            _service.Validate(
                CreateValidInput() with
                {
                    StatementDate =
                        new DateOnly(
                            2027,
                            1,
                            1),

                    DueDate =
                        new DateOnly(
                            2026,
                            8,
                            1)
                },
                Today);

        Assert.True(
            result.IsValid);

        Assert.True(
            result.RequiresReview);

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code ==
                "due_before_statement");

        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code ==
                "statement_date_future");
    }

    private static BillStatementValidationInput
        CreateValidInput()
    {
        return new BillStatementValidationInput(
            TotalAmount:
                104.99m,

            PeriodStart:
                new DateOnly(
                    2026,
                    7,
                    10),

            PeriodEnd:
                new DateOnly(
                    2026,
                    8,
                    9),

            StatementDate:
                new DateOnly(
                    2026,
                    8,
                    10),

            DueDate:
                new DateOnly(
                    2026,
                    8,
                    31),

            CurrencyCode:
                "USD");
    }
}