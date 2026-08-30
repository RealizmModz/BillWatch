using BillWatch.API.Services.Statements;

namespace BillWatch.Tests.Services;

public sealed class BillStatementAiGroundTruthScorerTests
{
    [Fact]
    public void Score_ProducesOnlyAggregateReadinessMetrics()
    {
        var expectedStatement =
            CompleteStatement();

        var expectedLineItems =
            new[]
            {
                new BillStatementStructuredLineItem(
                    "Internet service",
                    99.99m,
                    "Service")
            };

        var observations =
            Enumerable.Range(
                    0,
                    10)
                .Select(
                    _ =>
                        new BillStatementAiGroundTruthObservation(
                            ProviderKey:
                                "provider-a",
                            ExpectedStatement:
                                expectedStatement,
                            ExpectedLineItems:
                                expectedLineItems,
                            ProviderAttempted:
                                true,
                            ProviderFailed:
                                false,
                            ActualExtraction:
                                Extraction(
                                    CompleteStatement(),
                                    [
                                        new BillStatementStructuredLineItem(
                                            " INTERNET SERVICE ",
                                            99.99m,
                                            "service")
                                    ]),
                            AlertEvaluated:
                                true,
                            FalseAlert:
                                false))
                .Append(
                    new BillStatementAiGroundTruthObservation(
                        ProviderKey:
                            "provider-b",
                        ExpectedStatement:
                            expectedStatement,
                        ExpectedLineItems:
                            expectedLineItems,
                        ProviderAttempted:
                            true,
                        ProviderFailed:
                            true,
                        ActualExtraction:
                            null,
                        AlertEvaluated:
                            true,
                        FalseAlert:
                            true))
                .ToArray();

        var metrics =
            new BillStatementAiGroundTruthScorer()
                .Score(
                    observations);

        Assert.Equal(
            11,
            metrics.EvaluatedStatementCount);

        Assert.Equal(
            2,
            metrics.DistinctProviderCount);

        Assert.Equal(
            1,
            metrics.MinimumStatementsForAnyProvider);

        Assert.Equal(
            11,
            metrics.ProviderAttemptCount);

        Assert.Equal(
            1,
            metrics.ProviderFailureCount);

        Assert.Equal(
            10,
            metrics.ReadyCandidateStatementCount);

        Assert.Equal(
            70,
            metrics.CorrectFactCount);

        Assert.Equal(
            0,
            metrics.IncorrectFactCount);

        Assert.Equal(
            7,
            metrics.MissedFactCount);

        Assert.Equal(
            11,
            metrics.AlertEvaluatedStatementCount);

        Assert.Equal(
            1,
            metrics.FalseAlertStatementCount);
    }

    [Fact]
    public void WrongFact_CountsAsIncorrectPredictionAndMissedTruth()
    {
        var actualStatement =
            CompleteStatement() with
            {
                TotalAmount =
                    999.99m
            };

        var metrics =
            new BillStatementAiGroundTruthScorer()
                .Score(
                    [
                        Observation(
                            CompleteStatement(),
                            Extraction(
                                actualStatement,
                                []))
                    ]);

        Assert.Equal(
            5,
            metrics.CorrectFactCount);

        Assert.Equal(
            1,
            metrics.IncorrectFactCount);

        Assert.Equal(
            1,
            metrics.MissedFactCount);
    }

    [Fact]
    public void LineItems_AreComparedAsOrderIndependentCompositeFacts()
    {
        var first =
            new BillStatementStructuredLineItem(
                "Service",
                80m,
                "Service");

        var second =
            new BillStatementStructuredLineItem(
                "Tax",
                5m,
                "Tax");

        var metrics =
            new BillStatementAiGroundTruthScorer()
                .Score(
                    [
                        new BillStatementAiGroundTruthObservation(
                            ProviderKey:
                                "provider-a",
                            ExpectedStatement:
                                EmptyStatement(),
                            ExpectedLineItems:
                                [
                                    first,
                                    second
                                ],
                            ProviderAttempted:
                                true,
                            ProviderFailed:
                                false,
                            ActualExtraction:
                                Extraction(
                                    EmptyStatement(),
                                    [
                                        second,
                                        first
                                    ]),
                            AlertEvaluated:
                                false,
                            FalseAlert:
                                false)
                    ]);

        Assert.Equal(
            2,
            metrics.CorrectFactCount);

        Assert.Equal(
            0,
            metrics.IncorrectFactCount);

        Assert.Equal(
            0,
            metrics.MissedFactCount);
    }

    [Fact]
    public void FalseAlertWithoutEvaluation_IsRejected()
    {
        var observation =
            Observation(
                CompleteStatement(),
                Extraction(
                    CompleteStatement(),
                    [])) with
            {
                AlertEvaluated =
                    false,
                FalseAlert =
                    true
            };

        Assert.Throws<ArgumentException>(
            () =>
                new BillStatementAiGroundTruthScorer()
                    .Score(
                        [
                            observation
                        ]));
    }

    [Fact]
    public void EmptyCorpus_ProducesZerosAndCannotManufactureReadiness()
    {
        var metrics =
            new BillStatementAiGroundTruthScorer()
                .Score(
                    []);

        var readiness =
            new BillStatementAiShadowReadinessEvaluator()
                .Evaluate(
                    metrics,
                    BillStatementAiShadowReadinessPolicy
                        .PrivateBetaDefault);

        Assert.Equal(
            0,
            metrics.EvaluatedStatementCount);

        Assert.False(
            readiness.MeetsShadowAccuracyGate);
    }

    private static BillStatementAiGroundTruthObservation Observation(
        BillStatementStructuredData expected,
        BillStatementExtractionResult actual)
    {
        return new BillStatementAiGroundTruthObservation(
            ProviderKey:
                "provider-a",
            ExpectedStatement:
                expected,
            ExpectedLineItems:
                [],
            ProviderAttempted:
                true,
            ProviderFailed:
                false,
            ActualExtraction:
                actual,
            AlertEvaluated:
                true,
            FalseAlert:
                false);
    }

    private static BillStatementStructuredData CompleteStatement()
    {
        return new BillStatementStructuredData(
            TotalAmount:
                104.99m,
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
                new DateOnly(
                    2026,
                    8,
                    2),
            DueDate:
                new DateOnly(
                    2026,
                    8,
                    20),
            CurrencyCode:
                "USD",
            Confidence:
                BillStatementStructuredDataConfidence.StrongEvidence,
            MissingRequiredFields:
                []);
    }

    private static BillStatementStructuredData EmptyStatement()
    {
        return new BillStatementStructuredData(
            TotalAmount:
                null,
            BillingPeriodStart:
                null,
            BillingPeriodEnd:
                null,
            StatementDate:
                null,
            DueDate:
                null,
            CurrencyCode:
                string.Empty,
            Confidence:
                BillStatementStructuredDataConfidence.InsufficientEvidence,
            MissingRequiredFields:
                []);
    }

    private static BillStatementExtractionResult Extraction(
        BillStatementStructuredData statement,
        IReadOnlyList<BillStatementStructuredLineItem> lineItems)
    {
        return new BillStatementExtractionResult(
            Statement:
                statement,
            LineItems:
                lineItems,
            Source:
                BillStatementExtractionSource.AiAssisted,
            ExtractorVersion:
                "ground-truth-test",
            Evidence:
                []);
    }
}
