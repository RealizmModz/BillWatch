namespace BillWatch.API.Services.Statements;

/*
 * No-spend corpus coverage gate.
 *
 * Passing this gate means only that the private corpus is large and diverse
 * enough to begin an explicitly authorized offline provider evaluation. It
 * is not an accuracy result and never authorizes runtime or persistence use.
 */
public sealed class BillStatementAiPrivateCorpusCoverageGate
{
    public BillStatementAiPrivateCorpusCoverageDecision Evaluate(
        BillStatementAiPrivateCorpusCatalogSummary summary,
        BillStatementAiShadowReadinessPolicy readinessPolicy)
    {
        ArgumentNullException.ThrowIfNull(
            summary);

        ArgumentNullException.ThrowIfNull(
            readinessPolicy);

        ValidateSummary(
            summary);

        ValidatePolicy(
            readinessPolicy);

        var requiredCaseCount =
            Math.Max(
                readinessPolicy.MinimumEvaluatedStatementCount,
                Math.Max(
                    readinessPolicy.MinimumProviderAttemptCount,
                    readinessPolicy.MinimumAlertEvaluatedStatementCount));

        var failures =
            new List<string>();

        RequireMinimum(
            summary.CaseCount,
            requiredCaseCount,
            "private corpus case count",
            failures);

        RequireMinimum(
            summary.DistinctProviderCount,
            readinessPolicy.MinimumDistinctProviderCount,
            "private corpus provider count",
            failures);

        RequireMinimum(
            summary.MinimumCasesForAnyProvider,
            readinessPolicy.MinimumStatementsPerProvider,
            "smallest private corpus provider sample",
            failures);

        return new BillStatementAiPrivateCorpusCoverageDecision(
            MayBeginOfflineProviderEvaluation:
                failures.Count ==
                0,
            RequiredCaseCount:
                requiredCaseCount,
            Failures:
                failures.AsReadOnly());
    }

    private static void ValidateSummary(
        BillStatementAiPrivateCorpusCatalogSummary summary)
    {
        if (summary.CaseCount <
                0 ||
            summary.DistinctProviderCount <
                0 ||
            summary.MinimumCasesForAnyProvider <
                0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(summary),
                "Private corpus coverage counts cannot be negative.");
        }

        if (summary.DistinctProviderCount >
                summary.CaseCount ||
            summary.MinimumCasesForAnyProvider >
                summary.CaseCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(summary),
                "Private corpus coverage counts are inconsistent.");
        }

        if (summary.DistinctProviderCount ==
            0)
        {
            if (summary.CaseCount !=
                    0 ||
                summary.MinimumCasesForAnyProvider !=
                    0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(summary),
                    "An empty provider population cannot contain corpus cases.");
            }

            return;
        }

        if (summary.MinimumCasesForAnyProvider ==
                0 ||
            summary.MinimumCasesForAnyProvider >
                summary.CaseCount /
                summary.DistinctProviderCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(summary),
                "Private corpus provider coverage is inconsistent with the case count.");
        }
    }

    private static void ValidatePolicy(
        BillStatementAiShadowReadinessPolicy policy)
    {
        if (policy.MinimumEvaluatedStatementCount <=
                0 ||
            policy.MinimumProviderAttemptCount <=
                0 ||
            policy.MinimumAlertEvaluatedStatementCount <=
                0 ||
            policy.MinimumDistinctProviderCount <=
                0 ||
            policy.MinimumStatementsPerProvider <=
                0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                "Private corpus coverage thresholds must be positive.");
        }
    }

    private static void RequireMinimum(
        long actual,
        long required,
        string metricName,
        ICollection<string> failures)
    {
        if (actual >=
            required)
        {
            return;
        }

        failures.Add(
            $"The {metricName} is {actual}; at least {required} is required before provider evaluation.");
    }
}

public sealed record BillStatementAiPrivateCorpusCoverageDecision(
    bool MayBeginOfflineProviderEvaluation,
    long RequiredCaseCount,
    IReadOnlyList<string> Failures)
{
    public bool MayEnableRuntimeShadowMode =>
        false;

    public bool MayInfluencePersistence =>
        false;
}
