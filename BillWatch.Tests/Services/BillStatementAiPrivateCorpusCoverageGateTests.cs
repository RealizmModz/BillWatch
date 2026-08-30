using BillWatch.API.Services.Statements;

namespace BillWatch.Tests.Services;

public sealed class BillStatementAiPrivateCorpusCoverageGateTests
{
    [Fact]
    public void DefaultCoverageThresholds_PassAtTheExactBoundary()
    {
        var decision =
            new BillStatementAiPrivateCorpusCoverageGate()
                .Evaluate(
                    new BillStatementAiPrivateCorpusCatalogSummary(
                        CaseCount:
                            100,
                        DistinctProviderCount:
                            5,
                        MinimumCasesForAnyProvider:
                            10),
                    BillStatementAiShadowReadinessPolicy
                        .PrivateBetaDefault);

        Assert.True(
            decision.MayBeginOfflineProviderEvaluation);

        Assert.False(
            decision.MayEnableRuntimeShadowMode);

        Assert.False(
            decision.MayInfluencePersistence);

        Assert.Empty(
            decision.Failures);
    }

    [Fact]
    public void InsufficientCoverage_FailsEveryIndependentRequirement()
    {
        var decision =
            new BillStatementAiPrivateCorpusCoverageGate()
                .Evaluate(
                    new BillStatementAiPrivateCorpusCatalogSummary(
                        CaseCount:
                            25,
                        DistinctProviderCount:
                            3,
                        MinimumCasesForAnyProvider:
                            5),
                    BillStatementAiShadowReadinessPolicy
                        .PrivateBetaDefault);

        Assert.False(
            decision.MayBeginOfflineProviderEvaluation);

        Assert.Equal(
            3,
            decision.Failures.Count);
    }

    [Fact]
    public void LargestRequiredMeasurementPopulation_DeterminesCaseMinimum()
    {
        var policy =
            BillStatementAiShadowReadinessPolicy
                .PrivateBetaDefault with
            {
                MinimumEvaluatedStatementCount =
                    100,
                MinimumProviderAttemptCount =
                    120,
                MinimumAlertEvaluatedStatementCount =
                    110
            };

        var decision =
            new BillStatementAiPrivateCorpusCoverageGate()
                .Evaluate(
                    new BillStatementAiPrivateCorpusCatalogSummary(
                        CaseCount:
                            119,
                        DistinctProviderCount:
                            5,
                        MinimumCasesForAnyProvider:
                            10),
                    policy);

        Assert.False(
            decision.MayBeginOfflineProviderEvaluation);

        Assert.Equal(
            120,
            decision.RequiredCaseCount);
    }

    [Fact]
    public void ImpossibleProviderDistribution_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new BillStatementAiPrivateCorpusCoverageGate()
                    .Evaluate(
                        new BillStatementAiPrivateCorpusCatalogSummary(
                            CaseCount:
                                10,
                            DistinctProviderCount:
                                3,
                            MinimumCasesForAnyProvider:
                                4),
                        BillStatementAiShadowReadinessPolicy
                            .PrivateBetaDefault));
    }

    [Fact]
    public void CoverageDecision_CannotCarryCorpusPayloads()
    {
        var propertyNames =
            typeof(
                    BillStatementAiPrivateCorpusCoverageDecision)
                .GetProperties()
                .Select(
                    property =>
                        property.Name)
                .ToArray();

        Assert.DoesNotContain(
            propertyNames,
            name =>
                name.Contains(
                    "Text",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Path",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "ProviderKey",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "GroundTruth",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InvalidCoveragePolicy_IsRejected()
    {
        var invalidPolicy =
            BillStatementAiShadowReadinessPolicy
                .PrivateBetaDefault with
            {
                MinimumProviderAttemptCount =
                    0
            };

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new BillStatementAiPrivateCorpusCoverageGate()
                    .Evaluate(
                        new BillStatementAiPrivateCorpusCatalogSummary(
                            CaseCount:
                                100,
                            DistinctProviderCount:
                                5,
                            MinimumCasesForAnyProvider:
                                10),
                        invalidPolicy));
    }
}
