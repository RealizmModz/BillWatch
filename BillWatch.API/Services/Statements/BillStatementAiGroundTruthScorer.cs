namespace BillWatch.API.Services.Statements;

/*
 * Deterministically scores an in-memory ground-truth corpus and emits only
 * the aggregate counters consumed by the shadow-readiness gate.
 *
 * Corpus facts may be sensitive. Callers must load them from an approved
 * private source, must not log them, and must not persist observations or
 * model output through this scorer. This service is intentionally not
 * registered in Program.
 */
public sealed class BillStatementAiGroundTruthScorer
{
    private const int MaxProviderKeyLength =
        100;

    public BillStatementAiShadowReadinessMetrics Score(
        IReadOnlyList<BillStatementAiGroundTruthObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(
            observations);

        if (observations.Count ==
            0)
        {
            return new BillStatementAiShadowReadinessMetrics(
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
                AlertEvaluatedStatementCount:
                    0,
                FalseAlertStatementCount:
                    0);
        }

        var providerCounts =
            new Dictionary<string, long>(
                StringComparer.Ordinal);

        long providerAttemptCount =
            0;

        long providerFailureCount =
            0;

        long readyCandidateStatementCount =
            0;

        long correctFactCount =
            0;

        long incorrectFactCount =
            0;

        long missedFactCount =
            0;

        long alertEvaluatedStatementCount =
            0;

        long falseAlertStatementCount =
            0;

        foreach (var observation in
                 observations)
        {
            ArgumentNullException.ThrowIfNull(
                observation);

            ValidateObservation(
                observation);

            var providerKey =
                observation.ProviderKey
                    .Trim()
                    .ToUpperInvariant();

            providerCounts[providerKey] =
                providerCounts.GetValueOrDefault(
                    providerKey) +
                1;

            if (observation.ProviderAttempted)
            {
                providerAttemptCount++;
            }

            if (observation.ProviderFailed)
            {
                providerFailureCount++;
            }

            if (observation.ActualExtraction?.IsReadyForValidation ==
                true)
            {
                readyCandidateStatementCount++;
            }

            if (observation.AlertEvaluated)
            {
                alertEvaluatedStatementCount++;
            }

            if (observation.FalseAlert)
            {
                falseAlertStatementCount++;
            }

            var factCounts =
                ScoreExtractionFacts(
                    observation.ExpectedStatement,
                    observation.ExpectedLineItems,
                    observation.ActualExtraction);

            correctFactCount +=
                factCounts.Correct;

            incorrectFactCount +=
                factCounts.Incorrect;

            missedFactCount +=
                factCounts.Missed;
        }

        return new BillStatementAiShadowReadinessMetrics(
            EvaluatedStatementCount:
                observations.Count,
            DistinctProviderCount:
                providerCounts.Count,
            MinimumStatementsForAnyProvider:
                providerCounts.Values.Min(),
            ProviderAttemptCount:
                providerAttemptCount,
            ProviderFailureCount:
                providerFailureCount,
            ReadyCandidateStatementCount:
                readyCandidateStatementCount,
            CorrectFactCount:
                correctFactCount,
            IncorrectFactCount:
                incorrectFactCount,
            MissedFactCount:
                missedFactCount,
            AlertEvaluatedStatementCount:
                alertEvaluatedStatementCount,
            FalseAlertStatementCount:
                falseAlertStatementCount);
    }

    public static BillStatementAiFactScore ScoreExtractionFacts(
        BillStatementStructuredData expected,
        IReadOnlyList<BillStatementStructuredLineItem> expectedLineItems,
        BillStatementExtractionResult? actualExtraction)
    {
        ArgumentNullException.ThrowIfNull(
            expected);

        ArgumentNullException.ThrowIfNull(
            expectedLineItems);

        var score =
            new MutableFactScore();

        var actual =
            actualExtraction?.Statement;

        ScoreOptionalValue(
            expected.TotalAmount,
            actual?.TotalAmount,
            score);

        ScoreOptionalValue(
            expected.BillingPeriodStart,
            actual?.BillingPeriodStart,
            score);

        ScoreOptionalValue(
            expected.BillingPeriodEnd,
            actual?.BillingPeriodEnd,
            score);

        ScoreOptionalValue(
            expected.StatementDate,
            actual?.StatementDate,
            score);

        ScoreOptionalValue(
            expected.DueDate,
            actual?.DueDate,
            score);

        ScoreOptionalString(
            expected.CurrencyCode,
            actual?.CurrencyCode,
            score);

        ScoreLineItems(
            expectedLineItems,
            actualExtraction?.LineItems ??
                [],
            score);

        return new BillStatementAiFactScore(
            score.Correct,
            score.Incorrect,
            score.Missed);
    }

    private static void ScoreOptionalValue<T>(
        T? expected,
        T? actual,
        MutableFactScore score)
        where T : struct
    {
        if (expected.HasValue &&
            actual.HasValue)
        {
            if (EqualityComparer<T>.Default.Equals(
                    expected.Value,
                    actual.Value))
            {
                score.Correct++;
            }
            else
            {
                score.Incorrect++;
                score.Missed++;
            }

            return;
        }

        if (expected.HasValue)
        {
            score.Missed++;
        }
        else if (actual.HasValue)
        {
            score.Incorrect++;
        }
    }

    private static void ScoreOptionalString(
        string? expected,
        string? actual,
        MutableFactScore score)
    {
        var normalizedExpected =
            NormalizeOptionalString(
                expected);

        var normalizedActual =
            NormalizeOptionalString(
                actual);

        if (normalizedExpected is not null &&
            normalizedActual is not null)
        {
            if (string.Equals(
                    normalizedExpected,
                    normalizedActual,
                    StringComparison.Ordinal))
            {
                score.Correct++;
            }
            else
            {
                score.Incorrect++;
                score.Missed++;
            }

            return;
        }

        if (normalizedExpected is not null)
        {
            score.Missed++;
        }
        else if (normalizedActual is not null)
        {
            score.Incorrect++;
        }
    }

    private static void ScoreLineItems(
        IReadOnlyList<BillStatementStructuredLineItem> expected,
        IReadOnlyList<BillStatementStructuredLineItem> actual,
        MutableFactScore score)
    {
        var remainingActual =
            actual.Select(
                    NormalizeLineItem)
                .ToList();

        foreach (var expectedItem in
                 expected.Select(
                     NormalizeLineItem))
        {
            var matchIndex =
                remainingActual.FindIndex(
                    actualItem =>
                        actualItem ==
                        expectedItem);

            if (matchIndex >=
                0)
            {
                score.Correct++;
                remainingActual.RemoveAt(
                    matchIndex);
            }
            else
            {
                score.Missed++;
            }
        }

        score.Incorrect +=
            remainingActual.Count;
    }

    private static BillStatementAiNormalizedLineItem NormalizeLineItem(
        BillStatementStructuredLineItem item)
    {
        ArgumentNullException.ThrowIfNull(
            item);

        return new BillStatementAiNormalizedLineItem(
            NormalizeRequiredString(
                item.Description),
            decimal.Round(
                item.Amount,
                2,
                MidpointRounding.AwayFromZero),
            NormalizeOptionalString(
                item.Category));
    }

    private static string NormalizeRequiredString(
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value);

        return value
            .Trim()
            .ToUpperInvariant();
    }

    private static string? NormalizeOptionalString(
        string? value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? null
            : value
                .Trim()
                .ToUpperInvariant();
    }

    private static void ValidateObservation(
        BillStatementAiGroundTruthObservation observation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            observation.ProviderKey);

        if (observation.ProviderKey.Trim().Length >
            MaxProviderKeyLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(
                    observation.ProviderKey),
                $"Provider keys cannot exceed {MaxProviderKeyLength} characters.");
        }

        ArgumentNullException.ThrowIfNull(
            observation.ExpectedStatement);

        ArgumentNullException.ThrowIfNull(
            observation.ExpectedLineItems);

        if (!observation.ProviderAttempted &&
            (observation.ProviderFailed ||
                observation.ActualExtraction is not null))
        {
            throw new ArgumentException(
                "A provider result or failure requires a provider attempt.",
                nameof(observation));
        }

        if (observation.ProviderFailed &&
            observation.ActualExtraction is not null)
        {
            throw new ArgumentException(
                "A failed provider attempt cannot also have an extraction result.",
                nameof(observation));
        }

        if (observation.FalseAlert &&
            !observation.AlertEvaluated)
        {
            throw new ArgumentException(
                "A false alert requires an alert evaluation.",
                nameof(observation));
        }
    }

    private sealed class MutableFactScore
    {
        public long Correct { get; set; }

        public long Incorrect { get; set; }

        public long Missed { get; set; }
    }
}

public sealed record BillStatementAiGroundTruthObservation(
    string ProviderKey,
    BillStatementStructuredData ExpectedStatement,
    IReadOnlyList<BillStatementStructuredLineItem> ExpectedLineItems,
    bool ProviderAttempted,
    bool ProviderFailed,
    BillStatementExtractionResult? ActualExtraction,
    bool AlertEvaluated,
    bool FalseAlert);

public sealed record BillStatementAiFactScore(
    long Correct,
    long Incorrect,
    long Missed);

internal sealed record BillStatementAiNormalizedLineItem(
    string Description,
    decimal Amount,
    string? Category);
