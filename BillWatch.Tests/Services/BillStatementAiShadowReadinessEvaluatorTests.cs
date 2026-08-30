using BillWatch.API.Services.Statements;

namespace BillWatch.Tests.Services;

public sealed class BillStatementAiShadowReadinessEvaluatorTests
{
    [Fact]
    public void MeetsGate_OnlyWhenEveryThresholdPasses()
    {
        var result =
            new BillStatementAiShadowReadinessEvaluator()
                .Evaluate(
                    new BillStatementAiShadowReadinessMetrics(
                        EvaluatedStatementCount:
                            100,
                        DistinctProviderCount:
                            5,
                        MinimumStatementsForAnyProvider:
                            10,
                        ProviderAttemptCount:
                            100,
                        ProviderFailureCount:
                            5,
                        ReadyCandidateStatementCount:
                            85,
                        CorrectFactCount:
                            990,
                        IncorrectFactCount:
                            10,
                        MissedFactCount:
                            52,
                        FalseAlertStatementCount:
                            1),
                    BillStatementAiShadowReadinessPolicy
                        .PrivateBetaDefault);

        Assert.True(
            result.MeetsShadowAccuracyGate);

        Assert.Empty(
            result.Failures);

        Assert.Equal(
            0.99m,
            result.FactPrecision);

        Assert.True(
            result.FactRecall >=
                0.95m);
    }

    [Fact]
    public void InsufficientOrInaccurateCorpus_FailsClosedWithEveryReason()
    {
        var result =
            new BillStatementAiShadowReadinessEvaluator()
                .Evaluate(
                    new BillStatementAiShadowReadinessMetrics(
                        EvaluatedStatementCount:
                            20,
                        DistinctProviderCount:
                            2,
                        MinimumStatementsForAnyProvider:
                            3,
                        ProviderAttemptCount:
                            20,
                        ProviderFailureCount:
                            2,
                        ReadyCandidateStatementCount:
                            10,
                        CorrectFactCount:
                            80,
                        IncorrectFactCount:
                            20,
                        MissedFactCount:
                            20,
                        FalseAlertStatementCount:
                            2),
                    BillStatementAiShadowReadinessPolicy
                        .PrivateBetaDefault);

        Assert.False(
            result.MeetsShadowAccuracyGate);

        Assert.Equal(
            8,
            result.Failures.Count);

        Assert.Contains(
            result.Failures,
            failure =>
                failure.Contains(
                    "false-alert rate",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyCorpus_NeverPassesAndDoesNotDivideByZero()
    {
        var result =
            new BillStatementAiShadowReadinessEvaluator()
                .Evaluate(
                    new BillStatementAiShadowReadinessMetrics(
                        EvaluatedStatementCount:
                            0,
                        DistinctProviderCount:
                            0,
                        MinimumStatementsForAnyProvider:
                            0,
                        ProviderAttemptCount:
                            0,
                        ProviderFailureCount:
                            0,
                        ReadyCandidateStatementCount:
                            0,
                        CorrectFactCount:
                            0,
                        IncorrectFactCount:
                            0,
                        MissedFactCount:
                            0,
                        FalseAlertStatementCount:
                            0),
                    BillStatementAiShadowReadinessPolicy
                        .PrivateBetaDefault);

        Assert.False(
            result.MeetsShadowAccuracyGate);

        Assert.Equal(
            0m,
            result.FactPrecision);

        Assert.Equal(
            0m,
            result.FactRecall);
    }

    [Fact]
    public void InconsistentMetrics_AreRejected()
    {
        var metrics =
            new BillStatementAiShadowReadinessMetrics(
                EvaluatedStatementCount:
                    5,
                DistinctProviderCount:
                    2,
                MinimumStatementsForAnyProvider:
                    1,
                ProviderAttemptCount:
                    5,
                ProviderFailureCount:
                    1,
                ReadyCandidateStatementCount:
                    6,
                CorrectFactCount:
                    1,
                IncorrectFactCount:
                    0,
                MissedFactCount:
                    0,
                FalseAlertStatementCount:
                    0);

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new BillStatementAiShadowReadinessEvaluator()
                    .Evaluate(
                        metrics,
                        BillStatementAiShadowReadinessPolicy
                            .PrivateBetaDefault));
    }

    [Fact]
    public void ReadinessTypes_CannotCarrySensitiveStatementPayloads()
    {
        var propertyNames =
            typeof(
                    BillStatementAiShadowReadinessMetrics)
                .GetProperties()
                .Select(
                    property =>
                        property.Name)
                .Concat(
                    typeof(
                            BillStatementAiShadowReadinessResult)
                        .GetProperties()
                        .Select(
                            property =>
                                property.Name))
                .ToArray();

        Assert.DoesNotContain(
            propertyNames,
            name =>
                name.Contains(
                    "Text",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Prompt",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Evidence",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Account",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Response",
                    StringComparison.OrdinalIgnoreCase));
    }
}
