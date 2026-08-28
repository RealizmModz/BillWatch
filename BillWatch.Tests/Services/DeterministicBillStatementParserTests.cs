using BillWatch.API.Services.Statements;

namespace BillWatch.Tests.Services;

public sealed class DeterministicBillStatementParserTests
{
    private readonly DeterministicBillStatementParser
        _parser =
            new();

    [Fact]
    public void Parse_ExtractsExplicitStatementFields()
    {
        const string text =
            """
            MIDCO

            Statement Date: 08/10/2026
            Billing Period: 07/10/2026 - 08/09/2026
            Due Date: 08/31/2026

            Total Amount Due: $104.99
            """;

        var result =
            _parser.Parse(
                text);

        Assert.Equal(
            104.99m,
            result.TotalAmount);

        Assert.Equal(
            new DateOnly(
                2026,
                7,
                10),
            result.BillingPeriodStart);

        Assert.Equal(
            new DateOnly(
                2026,
                8,
                9),
            result.BillingPeriodEnd);

        Assert.Equal(
            new DateOnly(
                2026,
                8,
                10),
            result.StatementDate);

        Assert.Equal(
            new DateOnly(
                2026,
                8,
                31),
            result.DueDate);

        Assert.Equal(
            "USD",
            result.CurrencyCode);

        Assert.Equal(
            BillStatementStructuredDataConfidence.StrongEvidence,
            result.Confidence);

        Assert.True(
            result.IsReadyForPersistence);

        Assert.Empty(
            result.MissingRequiredFields);
    }

    [Fact]
    public void Parse_SupportsTextualBillingDates()
    {
        const string text =
            """
            Statement Date: September 2, 2026
            Service Period: August 1, 2026 through August 31, 2026
            Payment Due: September 20, 2026
            Amount Due: $86.42
            """;

        var result =
            _parser.Parse(
                text);

        Assert.Equal(
            86.42m,
            result.TotalAmount);

        Assert.Equal(
            new DateOnly(
                2026,
                8,
                1),
            result.BillingPeriodStart);

        Assert.Equal(
            new DateOnly(
                2026,
                8,
                31),
            result.BillingPeriodEnd);

        Assert.Equal(
            new DateOnly(
                2026,
                9,
                2),
            result.StatementDate);

        Assert.Equal(
            new DateOnly(
                2026,
                9,
                20),
            result.DueDate);

        Assert.True(
            result.IsReadyForPersistence);
    }

    [Fact]
    public void Parse_PrioritizesStrongestAmountLabel()
    {
        const string text =
            """
            Balance Due: $55.00
            Total Amount Due: $104.99
            Billing Period: 07/01/2026 - 07/31/2026
            """;

        var result =
            _parser.Parse(
                text);

        Assert.Equal(
            104.99m,
            result.TotalAmount);
    }

    [Fact]
    public void Parse_SupportsThousandsSeparators()
    {
        const string text =
            """
            Total Due: $1,284.37
            Billing Cycle: 08/01/2026 - 08/31/2026
            """;

        var result =
            _parser.Parse(
                text);

        Assert.Equal(
            1284.37m,
            result.TotalAmount);

        Assert.True(
            result.IsReadyForPersistence);
    }

    [Fact]
    public void Parse_DoesNotGuessFromUnlabelledMoney()
    {
        const string text =
            """
            Thank you for being a customer.

            Your service this month was $129.99.

            Billing Period: 08/01/2026 - 08/31/2026
            """;

        var result =
            _parser.Parse(
                text);

        Assert.Null(
            result.TotalAmount);

        Assert.False(
            result.IsReadyForPersistence);

        Assert.Contains(
            nameof(
                BillStatementStructuredData.TotalAmount),
            result.MissingRequiredFields);
    }

    [Fact]
    public void Parse_RejectsBackwardsBillingPeriod()
    {
        const string text =
            """
            Total Amount Due: $79.99
            Billing Period: 08/31/2026 - 08/01/2026
            """;

        var result =
            _parser.Parse(
                text);

        Assert.Null(
            result.BillingPeriodStart);

        Assert.Null(
            result.BillingPeriodEnd);

        Assert.False(
            result.IsReadyForPersistence);
    }

    [Fact]
    public void Parse_EmptyTextReturnsInsufficientEvidence()
    {
        var result =
            _parser.Parse(
                "   ");

        Assert.Null(
            result.TotalAmount);

        Assert.Null(
            result.BillingPeriodStart);

        Assert.Null(
            result.BillingPeriodEnd);

        Assert.Null(
            result.StatementDate);

        Assert.Null(
            result.DueDate);

        Assert.False(
            result.IsReadyForPersistence);

        Assert.Equal(
            BillStatementStructuredDataConfidence.InsufficientEvidence,
            result.Confidence);

        Assert.Equal(
            3,
            result.MissingRequiredFields.Count);
    }
}