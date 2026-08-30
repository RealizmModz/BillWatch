namespace BillWatch.API.Services.Statements;

/*
 * Offline deterministic baseline for explicitly selected private corpus
 * cases. This evaluator makes no AI/provider calls, stores nothing, and
 * returns aggregate metrics only. It is intentionally not registered.
 */
public sealed class BillStatementDeterministicPrivateCorpusEvaluator
{
    private const int MaxCasesPerRun =
        1_000;

    private readonly BillStatementAiPrivateCorpusLoader _loader;

    private readonly DeterministicBillStatementExtractionService
        _deterministicExtractor;

    public BillStatementDeterministicPrivateCorpusEvaluator(
        BillStatementAiPrivateCorpusLoader loader,
        DeterministicBillStatementExtractionService deterministicExtractor)
    {
        ArgumentNullException.ThrowIfNull(
            loader);

        ArgumentNullException.ThrowIfNull(
            deterministicExtractor);

        _loader =
            loader;

        _deterministicExtractor =
            deterministicExtractor;
    }

    public async Task<BillStatementDeterministicCorpusBaseline> EvaluateAsync(
        string corpusRootDirectory,
        IReadOnlyList<string> caseIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            caseIds);

        if (caseIds.Count is <
                1 or >
                MaxCasesPerRun)
        {
            throw new ArgumentOutOfRangeException(
                nameof(caseIds),
                $"A deterministic corpus run requires between 1 and {MaxCasesPerRun} cases.");
        }

        if (caseIds.Any(
                string.IsNullOrWhiteSpace) ||
            caseIds.Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count() !=
                caseIds.Count)
        {
            throw new ArgumentException(
                "Deterministic corpus case identifiers must be non-empty and unique.",
                nameof(caseIds));
        }

        long readyStatementCount =
            0;

        long correctFactCount =
            0;

        long incorrectFactCount =
            0;

        long missedFactCount =
            0;

        foreach (var caseId in
                 caseIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var corpusCase =
                await _loader.LoadAsync(
                    corpusRootDirectory,
                    caseId,
                    cancellationToken);

            var extraction =
                await _deterministicExtractor.ExtractAsync(
                    new BillStatementExtractionRequest(
                        DocumentText:
                            corpusCase.StatementText,
                        Hints:
                            new BillStatementExtractionHints(
                                ExpectedProviderName:
                                    null,
                                ExpectedCategory:
                                    null)),
                    cancellationToken);

            if (extraction.IsReadyForValidation)
            {
                readyStatementCount++;
            }

            var factScore =
                BillStatementAiGroundTruthScorer
                    .ScoreExtractionFacts(
                        corpusCase.ExpectedStatement,
                        corpusCase.ExpectedLineItems,
                        extraction);

            correctFactCount +=
                factScore.Correct;

            incorrectFactCount +=
                factScore.Incorrect;

            missedFactCount +=
                factScore.Missed;
        }

        var predictedFactCount =
            correctFactCount +
            incorrectFactCount;

        var groundTruthFactCount =
            correctFactCount +
            missedFactCount;

        return new BillStatementDeterministicCorpusBaseline(
            EvaluatedStatementCount:
                caseIds.Count,
            ReadyStatementCount:
                readyStatementCount,
            CorrectFactCount:
                correctFactCount,
            IncorrectFactCount:
                incorrectFactCount,
            MissedFactCount:
                missedFactCount,
            ReadyStatementRate:
                Divide(
                    readyStatementCount,
                    caseIds.Count),
            FactPrecision:
                Divide(
                    correctFactCount,
                    predictedFactCount),
            FactRecall:
                Divide(
                    correctFactCount,
                    groundTruthFactCount));
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
}

public sealed record BillStatementDeterministicCorpusBaseline(
    long EvaluatedStatementCount,
    long ReadyStatementCount,
    long CorrectFactCount,
    long IncorrectFactCount,
    long MissedFactCount,
    decimal ReadyStatementRate,
    decimal FactPrecision,
    decimal FactRecall);
