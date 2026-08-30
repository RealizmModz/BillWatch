using BillWatch.API.Services.Statements;

namespace BillWatch.Tests.Services;

public sealed class BillStatementAiCandidateConversionServiceTests
{
    [Fact]
    public void ValidEvidenceBackedCandidate_MapsIntoExistingStructuredModel()
    {
        const string documentText =
            """
            MIDCO
            Billing period August 1, 2026 through August 31, 2026
            Internet service $99.994
            Equipment fee $5.00
            AutoPay discount -$10.00
            Total due $94.994
            USD
            """;

        var candidate =
            new BillStatementAiCandidate(
                ProviderName:
                    "MIDCO",

                AccountIdentifierSuffix:
                    null,

                BillingPeriodStart:
                    new DateOnly(
                        2026,
                        8,
                        1),

                BillingPeriodEnd:
                    new DateOnly(
                        2026,
                        8,
                        31),

                StatementDate:
                    null,

                DueDate:
                    null,

                PreviousBalance:
                    null,

                Payments:
                    null,

                CurrentCharges:
                    null,

                TotalDue:
                    94.994m,

                CurrencyCode:
                    "usd",

                PlanOrService:
                    null,

                UsageSummary:
                    null,

                LineItems:
                    [
                        new BillStatementAiLineItemCandidate(
                            Description:
                                "Internet service",

                            Amount:
                                99.994m,

                            Kind:
                                BillStatementAiLineItemKind.Service),

                        new BillStatementAiLineItemCandidate(
                            Description:
                                "Equipment fee",

                            Amount:
                                5m,

                            Kind:
                                BillStatementAiLineItemKind.Fee),

                        new BillStatementAiLineItemCandidate(
                            Description:
                                "AutoPay discount",

                            Amount:
                                -10m,

                            Kind:
                                BillStatementAiLineItemKind.Promotion)
                    ],

                Evidence:
                    [
                        Evidence(
                            BillStatementAiFactKeys.ProviderName,
                            "MIDCO"),

                        Evidence(
                            BillStatementAiFactKeys.BillingPeriodStart,
                            "Billing period August 1, 2026 through August 31, 2026"),

                        Evidence(
                            BillStatementAiFactKeys.BillingPeriodEnd,
                            "Billing period August 1, 2026 through August 31, 2026"),

                        Evidence(
                            BillStatementAiFactKeys.TotalDue,
                            "Total due $94.994"),

                        Evidence(
                            BillStatementAiFactKeys.CurrencyCode,
                            "USD"),

                        Evidence(
                            BillStatementAiFactKeys.LineItemDescription(0),
                            "Internet service $99.994"),

                        Evidence(
                            BillStatementAiFactKeys.LineItemAmount(0),
                            "Internet service $99.994"),

                        Evidence(
                            BillStatementAiFactKeys.LineItemDescription(1),
                            "Equipment fee $5.00"),

                        Evidence(
                            BillStatementAiFactKeys.LineItemAmount(1),
                            "Equipment fee $5.00"),

                        Evidence(
                            BillStatementAiFactKeys.LineItemDescription(2),
                            "AutoPay discount -$10.00"),

                        Evidence(
                            BillStatementAiFactKeys.LineItemAmount(2),
                            "AutoPay discount -$10.00")
                    ],

                ModelConfidence:
                    BillStatementAiModelConfidence.Low);

        var service =
            CreateService();

        var result =
            service.Convert(
                documentText,
                candidate);

        Assert.True(
            result.IsAccepted,
            string.Join(
                Environment.NewLine,
                result.Errors));

        var extraction =
            Assert.IsType<BillStatementExtractionResult>(
                result.Extraction);

        Assert.Equal(
            BillStatementExtractionSource.AiAssisted,
            extraction.Source);

        Assert.Equal(
            94.99m,
            extraction.Statement.TotalAmount);

        Assert.Equal(
            "USD",
            extraction.Statement.CurrencyCode);

        Assert.Equal(
            BillStatementStructuredDataConfidence.StrongEvidence,
            extraction.Statement.Confidence);

        Assert.True(
            extraction.IsReadyForValidation);

        Assert.Equal(
            3,
            extraction.LineItems.Count);

        Assert.Equal(
            99.99m,
            extraction.LineItems[0].Amount);

        Assert.Equal(
            "Service",
            extraction.LineItems[0].Category);

        Assert.Equal(
            "Fee",
            extraction.LineItems[1].Category);

        Assert.Equal(
            "Discount",
            extraction.LineItems[2].Category);
    }

    [Fact]
    public void HighModelConfidence_DoesNotMakeIncompleteCandidateReady()
    {
        const string documentText =
            """
            MIDCO
            Total due $104.99
            """;

        var candidate =
            EmptyCandidate() with
            {
                ProviderName =
                    "MIDCO",

                TotalDue =
                    104.99m,

                Evidence =
                    [
                        Evidence(
                            BillStatementAiFactKeys.ProviderName,
                            "MIDCO"),

                        Evidence(
                            BillStatementAiFactKeys.TotalDue,
                            "Total due $104.99")
                    ],

                ModelConfidence =
                    BillStatementAiModelConfidence.High
            };

        var result =
            CreateService().Convert(
                documentText,
                candidate);

        Assert.True(
            result.IsAccepted,
            string.Join(
                Environment.NewLine,
                result.Errors));

        var extraction =
            Assert.IsType<BillStatementExtractionResult>(
                result.Extraction);

        Assert.False(
            extraction.IsReadyForValidation);

        Assert.Equal(
            BillStatementStructuredDataConfidence.Partial,
            extraction.Statement.Confidence);

        Assert.Contains(
            nameof(
                BillStatementStructuredData.BillingPeriodStart),
            extraction.Statement.MissingRequiredFields);

        Assert.Contains(
            nameof(
                BillStatementStructuredData.BillingPeriodEnd),
            extraction.Statement.MissingRequiredFields);

        Assert.Contains(
            nameof(
                BillStatementStructuredData.CurrencyCode),
            extraction.Statement.MissingRequiredFields);
    }

    [Fact]
    public void MissingCurrency_IsNotSilentlyDefaultedToUsd()
    {
        const string documentText =
            """
            Billing period August 1, 2026 through August 31, 2026
            Total due $104.99
            """;

        var candidate =
            EmptyCandidate() with
            {
                BillingPeriodStart =
                    new DateOnly(
                        2026,
                        8,
                        1),

                BillingPeriodEnd =
                    new DateOnly(
                        2026,
                        8,
                        31),

                TotalDue =
                    104.99m,

                Evidence =
                    [
                        Evidence(
                            BillStatementAiFactKeys.BillingPeriodStart,
                            "Billing period August 1, 2026 through August 31, 2026"),

                        Evidence(
                            BillStatementAiFactKeys.BillingPeriodEnd,
                            "Billing period August 1, 2026 through August 31, 2026"),

                        Evidence(
                            BillStatementAiFactKeys.TotalDue,
                            "Total due $104.99")
                    ]
            };

        var result =
            CreateService().Convert(
                documentText,
                candidate);

        Assert.True(
            result.IsAccepted,
            string.Join(
                Environment.NewLine,
                result.Errors));

        var extraction =
            Assert.IsType<BillStatementExtractionResult>(
                result.Extraction);

        Assert.False(
            extraction.IsReadyForValidation);

        Assert.Equal(
            string.Empty,
            extraction.Statement.CurrencyCode);

        Assert.Contains(
            nameof(
                BillStatementStructuredData.CurrencyCode),
            extraction.Statement.MissingRequiredFields);
    }

    [Fact]
    public void UnsupportedEvidence_IsRejectedBeforeMapping()
    {
        const string documentText =
            """
            MIDCO
            Total due $104.99
            """;

        var candidate =
            EmptyCandidate() with
            {
                TotalDue =
                    199.99m,

                Evidence =
                    [
                        Evidence(
                            BillStatementAiFactKeys.TotalDue,
                            "Total due $199.99")
                    ]
            };

        var result =
            CreateService().Convert(
                documentText,
                candidate);

        Assert.False(
            result.IsAccepted);

        Assert.Null(
            result.Extraction);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    "was not found in the source document",
                    StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        BillStatementAiLineItemKind.Discount)]
    [InlineData(
        BillStatementAiLineItemKind.Credit)]
    [InlineData(
        BillStatementAiLineItemKind.Promotion)]
    public void ReductionKinds_MapToExistingDiscountDomainCategory(
        BillStatementAiLineItemKind kind)
    {
        const string documentText =
            """
            Billing period August 1, 2026 through August 31, 2026
            Total due $90.00
            Loyalty credit -$10.00
            USD
            """;

        var candidate =
            EmptyCandidate() with
            {
                BillingPeriodStart =
                    new DateOnly(
                        2026,
                        8,
                        1),

                BillingPeriodEnd =
                    new DateOnly(
                        2026,
                        8,
                        31),

                TotalDue =
                    90m,

                CurrencyCode =
                    "USD",

                LineItems =
                    [
                        new BillStatementAiLineItemCandidate(
                            "Loyalty credit",
                            -10m,
                            kind)
                    ],

                Evidence =
                    [
                        Evidence(
                            BillStatementAiFactKeys.BillingPeriodStart,
                            "Billing period August 1, 2026 through August 31, 2026"),

                        Evidence(
                            BillStatementAiFactKeys.BillingPeriodEnd,
                            "Billing period August 1, 2026 through August 31, 2026"),

                        Evidence(
                            BillStatementAiFactKeys.TotalDue,
                            "Total due $90.00"),

                        Evidence(
                            BillStatementAiFactKeys.CurrencyCode,
                            "USD"),

                        Evidence(
                            BillStatementAiFactKeys.LineItemDescription(0),
                            "Loyalty credit -$10.00"),

                        Evidence(
                            BillStatementAiFactKeys.LineItemAmount(0),
                            "Loyalty credit -$10.00")
                    ]
            };

        var result =
            CreateService().Convert(
                documentText,
                candidate);

        Assert.True(
            result.IsAccepted,
            string.Join(
                Environment.NewLine,
                result.Errors));

        var extraction =
            Assert.IsType<BillStatementExtractionResult>(
                result.Extraction);

        var item =
            Assert.Single(
                extraction.LineItems);

        Assert.Equal(
            "Discount",
            item.Category);
    }

    private static BillStatementAiCandidateConversionService
        CreateService()
    {
        return new BillStatementAiCandidateConversionService(
            new BillStatementAiCandidateValidator());
    }

    private static BillStatementAiEvidence Evidence(
        string factKey,
        string sourceExcerpt)
    {
        return new BillStatementAiEvidence(
            factKey,
            sourceExcerpt);
    }

    private static BillStatementAiCandidate EmptyCandidate()
    {
        return new BillStatementAiCandidate(
            ProviderName:
                null,

            AccountIdentifierSuffix:
                null,

            BillingPeriodStart:
                null,

            BillingPeriodEnd:
                null,

            StatementDate:
                null,

            DueDate:
                null,

            PreviousBalance:
                null,

            Payments:
                null,

            CurrentCharges:
                null,

            TotalDue:
                null,

            CurrencyCode:
                null,

            PlanOrService:
                null,

            UsageSummary:
                null,

            LineItems:
                [],

            Evidence:
                [],

            ModelConfidence:
                BillStatementAiModelConfidence.Unknown);
    }
}
