namespace BillWatch.API.Services.Statements;

/*
 * Pure, offline accuracy gate for a future shadow-mode rollout.
 *
 * This evaluator consumes aggregate ground-truth results only. It does not
 * receive statement text, extracted facts, account data, or provider output,
 * and it is intentionally not registered in Program.
 *
 * Passing this gate is not permission to persist AI-derived statement facts.
 */
public sealed class BillStatementAiShadowReadinessEvaluator
{
    public BillStatementAiShadowReadinessResult Evaluate(
        BillStatementAiShadowReadinessMetrics metrics,
        BillStatementAiShadowReadinessPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(
            metrics);

        ArgumentNullException.ThrowIfNull(
            policy);

        ValidateMetrics(
            metrics);

        ValidatePolicy(
            policy);

        var predictedFactCount =
            metrics.CorrectFactCount +
            metrics.IncorrectFactCount;

        var groundTruthFactCount =
            metrics.CorrectFactCount +
            metrics.MissedFactCount;

        var factPrecision =
            Divide(
                metrics.CorrectFactCount,
                predictedFactCount);

        var factRecall =
            Divide(
                metrics.CorrectFactCount,
                groundTruthFactCount);

        var readyCandidateRate =
            Divide(
                metrics.ReadyCandidateStatementCount,
                metrics.ProviderAttemptCount);

        var falseAlertRate =
            Divide(
                metrics.FalseAlertStatementCount,
                metrics.AlertEvaluatedStatementCount);

        var providerFailureRate =
            Divide(
                metrics.ProviderFailureCount,
                metrics.ProviderAttemptCount);

        var failures =
            new List<string>();

        RequireMinimum(
            metrics.EvaluatedStatementCount,
            policy.MinimumEvaluatedStatementCount,
            "evaluated statement count",
            failures);

        RequireMinimum(
            metrics.DistinctProviderCount,
            policy.MinimumDistinctProviderCount,
            "distinct provider count",
            failures);

        RequireMinimum(
            metrics.MinimumStatementsForAnyProvider,
            policy.MinimumStatementsPerProvider,
            "minimum statements per provider",
            failures);

        RequireMinimum(
            metrics.ProviderAttemptCount,
            policy.MinimumProviderAttemptCount,
            "provider attempt count",
            failures);

        RequireMinimum(
            metrics.AlertEvaluatedStatementCount,
            policy.MinimumAlertEvaluatedStatementCount,
            "alert-evaluated statement count",
            failures);

        RequireRateAtLeast(
            factPrecision,
            policy.MinimumFactPrecision,
            "fact precision",
            failures);

        RequireRateAtLeast(
            factRecall,
            policy.MinimumFactRecall,
            "fact recall",
            failures);

        RequireRateAtLeast(
            readyCandidateRate,
            policy.MinimumReadyCandidateRate,
            "ready-candidate rate",
            failures);

        RequireRateAtMost(
            falseAlertRate,
            policy.MaximumFalseAlertRate,
            "false-alert rate",
            failures);

        RequireRateAtMost(
            providerFailureRate,
            policy.MaximumProviderFailureRate,
            "provider failure rate",
            failures);

        return new BillStatementAiShadowReadinessResult(
            MeetsShadowAccuracyGate:
                failures.Count ==
                0,
            FactPrecision:
                factPrecision,
            FactRecall:
                factRecall,
            ReadyCandidateRate:
                readyCandidateRate,
            FalseAlertRate:
                falseAlertRate,
            ProviderFailureRate:
                providerFailureRate,
            Failures:
                failures.AsReadOnly());
    }

    private static decimal Divide(
        long numerator,
        long denominator)
    {
        return denominator ==
                0
            ? 0m
            : decimal.Divide(
                numerator,
                denominator);
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
            $"The {metricName} is {actual}; at least {required} is required.");
    }

    private static void RequireRateAtLeast(
        decimal actual,
        decimal required,
        string metricName,
        ICollection<string> failures)
    {
        if (actual >=
            required)
        {
            return;
        }

        failures.Add(
            $"The {metricName} is {actual:P2}; at least {required:P2} is required.");
    }

    private static void RequireRateAtMost(
        decimal actual,
        decimal required,
        string metricName,
        ICollection<string> failures)
    {
        if (actual <=
            required)
        {
            return;
        }

        failures.Add(
            $"The {metricName} is {actual:P2}; no more than {required:P2} is allowed.");
    }

    private static void ValidateMetrics(
        BillStatementAiShadowReadinessMetrics metrics)
    {
        ValidateNonNegative(
            metrics.EvaluatedStatementCount,
            nameof(
                metrics.EvaluatedStatementCount));

        ValidateNonNegative(
            metrics.DistinctProviderCount,
            nameof(
                metrics.DistinctProviderCount));

        ValidateNonNegative(
            metrics.MinimumStatementsForAnyProvider,
            nameof(
                metrics.MinimumStatementsForAnyProvider));

        ValidateNonNegative(
            metrics.ProviderAttemptCount,
            nameof(
                metrics.ProviderAttemptCount));

        ValidateNonNegative(
            metrics.ProviderFailureCount,
            nameof(
                metrics.ProviderFailureCount));

        ValidateNonNegative(
            metrics.ReadyCandidateStatementCount,
            nameof(
                metrics.ReadyCandidateStatementCount));

        ValidateNonNegative(
            metrics.CorrectFactCount,
            nameof(
                metrics.CorrectFactCount));

        ValidateNonNegative(
            metrics.IncorrectFactCount,
            nameof(
                metrics.IncorrectFactCount));

        ValidateNonNegative(
            metrics.MissedFactCount,
            nameof(
                metrics.MissedFactCount));

        ValidateNonNegative(
            metrics.FalseAlertStatementCount,
            nameof(
                metrics.FalseAlertStatementCount));

        ValidateNonNegative(
            metrics.AlertEvaluatedStatementCount,
            nameof(
                metrics.AlertEvaluatedStatementCount));

        RequireNotGreaterThan(
            metrics.DistinctProviderCount,
            metrics.EvaluatedStatementCount,
            nameof(
                metrics.DistinctProviderCount));

        RequireNotGreaterThan(
            metrics.MinimumStatementsForAnyProvider,
            metrics.EvaluatedStatementCount,
            nameof(
                metrics.MinimumStatementsForAnyProvider));

        RequireNotGreaterThan(
            metrics.ProviderAttemptCount,
            metrics.EvaluatedStatementCount,
            nameof(
                metrics.ProviderAttemptCount));

        RequireNotGreaterThan(
            metrics.ProviderFailureCount,
            metrics.ProviderAttemptCount,
            nameof(
                metrics.ProviderFailureCount));

        RequireNotGreaterThan(
            metrics.ReadyCandidateStatementCount,
            metrics.ProviderAttemptCount,
            nameof(
                metrics.ReadyCandidateStatementCount));

        RequireNotGreaterThan(
            metrics.FalseAlertStatementCount,
            metrics.AlertEvaluatedStatementCount,
            nameof(
                metrics.FalseAlertStatementCount));

        RequireNotGreaterThan(
            metrics.AlertEvaluatedStatementCount,
            metrics.EvaluatedStatementCount,
            nameof(
                metrics.AlertEvaluatedStatementCount));
    }

    private static void ValidatePolicy(
        BillStatementAiShadowReadinessPolicy policy)
    {
        if (policy.MinimumEvaluatedStatementCount <=
                0 ||
            policy.MinimumDistinctProviderCount <=
                0 ||
            policy.MinimumStatementsPerProvider <=
                0 ||
            policy.MinimumProviderAttemptCount <=
                0 ||
            policy.MinimumAlertEvaluatedStatementCount <=
                0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                "Readiness sample minimums must be positive.");
        }

        ValidateRate(
            policy.MinimumFactPrecision,
            nameof(
                policy.MinimumFactPrecision));

        ValidateRate(
            policy.MinimumFactRecall,
            nameof(
                policy.MinimumFactRecall));

        ValidateRate(
            policy.MinimumReadyCandidateRate,
            nameof(
                policy.MinimumReadyCandidateRate));

        ValidateRate(
            policy.MaximumFalseAlertRate,
            nameof(
                policy.MaximumFalseAlertRate));

        ValidateRate(
            policy.MaximumProviderFailureRate,
            nameof(
                policy.MaximumProviderFailureRate));
    }

    private static void ValidateNonNegative(
        long value,
        string parameterName)
    {
        if (value <
            0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Metric counts cannot be negative.");
        }
    }

    private static void RequireNotGreaterThan(
        long value,
        long maximum,
        string parameterName)
    {
        if (value <=
            maximum)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            parameterName,
            "The metric is inconsistent with its population count.");
    }

    private static void ValidateRate(
        decimal value,
        string parameterName)
    {
        if (value is >=
                0m and <=
                1m)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            parameterName,
            "A readiness rate must be between zero and one.");
    }
}

public sealed record BillStatementAiShadowReadinessMetrics(
    long EvaluatedStatementCount,
    long DistinctProviderCount,
    long MinimumStatementsForAnyProvider,
    long ProviderAttemptCount,
    long ProviderFailureCount,
    long ReadyCandidateStatementCount,
    long CorrectFactCount,
    long IncorrectFactCount,
    long MissedFactCount,
    long AlertEvaluatedStatementCount,
    long FalseAlertStatementCount);

public sealed record BillStatementAiShadowReadinessPolicy(
    long MinimumEvaluatedStatementCount,
    long MinimumDistinctProviderCount,
    long MinimumStatementsPerProvider,
    long MinimumProviderAttemptCount,
    long MinimumAlertEvaluatedStatementCount,
    decimal MinimumFactPrecision,
    decimal MinimumFactRecall,
    decimal MinimumReadyCandidateRate,
    decimal MaximumFalseAlertRate,
    decimal MaximumProviderFailureRate)
{
    /*
     * Conservative starting thresholds for private-beta shadow evaluation.
     * Changing them is a product/security decision, not a model decision.
     */
    public static BillStatementAiShadowReadinessPolicy PrivateBetaDefault
    {
        get;
    } =
        new(
            MinimumEvaluatedStatementCount:
                100,
            MinimumDistinctProviderCount:
                5,
            MinimumStatementsPerProvider:
                10,
            MinimumProviderAttemptCount:
                100,
            MinimumAlertEvaluatedStatementCount:
                100,
            MinimumFactPrecision:
                0.99m,
            MinimumFactRecall:
                0.95m,
            MinimumReadyCandidateRate:
                0.85m,
            MaximumFalseAlertRate:
                0.01m,
            MaximumProviderFailureRate:
                0.05m);
}

public sealed record BillStatementAiShadowReadinessResult(
    bool MeetsShadowAccuracyGate,
    decimal FactPrecision,
    decimal FactRecall,
    decimal ReadyCandidateRate,
    decimal FalseAlertRate,
    decimal ProviderFailureRate,
    IReadOnlyList<string> Failures);
