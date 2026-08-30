namespace BillWatch.API.Services.Statements;

/*
 * Offline-only provider evaluator for an explicitly approved private
 * statement corpus.
 *
 * This class is intentionally NOT registered in Program.
 *
 * Safety properties:
 *
 * - requires explicit authorization before any provider call
 * - validates every selected corpus case before any provider call
 * - requires the existing aggregate coverage gate to pass first
 * - makes at most one provider call per selected case
 * - validates model output through BillWatch's deterministic trust boundary
 * - treats rejected AI candidates as unusable rather than trusted facts
 * - stores nothing
 * - logs nothing
 * - returns aggregate metrics only
 * - never authorizes runtime shadow mode
 * - never authorizes AI-derived persistence
 *
 * Provider keys, statement text, expected facts, model output, evidence,
 * account information, and case identifiers never appear in the result.
 */
public sealed class BillStatementAiPrivateCorpusProviderEvaluator
{
    private const int MaxCasesPerRun =
        1_000;

    private readonly BillStatementAiPrivateCorpusLoader
        _loader;

    private readonly BillStatementAiPrivateCorpusCoverageGate
        _coverageGate;

    private readonly IBillStatementAiExtractor
        _aiExtractor;

    private readonly BillStatementAiCandidateConversionService
        _conversionService;

    private readonly BillStatementAiGroundTruthScorer
        _groundTruthScorer;

    public BillStatementAiPrivateCorpusProviderEvaluator(
        BillStatementAiPrivateCorpusLoader loader,
        BillStatementAiPrivateCorpusCoverageGate coverageGate,
        IBillStatementAiExtractor aiExtractor,
        BillStatementAiCandidateConversionService conversionService,
        BillStatementAiGroundTruthScorer groundTruthScorer)
    {
        ArgumentNullException.ThrowIfNull(
            loader);

        ArgumentNullException.ThrowIfNull(
            coverageGate);

        ArgumentNullException.ThrowIfNull(
            aiExtractor);

        ArgumentNullException.ThrowIfNull(
            conversionService);

        ArgumentNullException.ThrowIfNull(
            groundTruthScorer);

        _loader =
            loader;

        _coverageGate =
            coverageGate;

        _aiExtractor =
            aiExtractor;

        _conversionService =
            conversionService;

        _groundTruthScorer =
            groundTruthScorer;
    }

    public async Task<BillStatementAiPrivateCorpusProviderEvaluationResult>
        EvaluateAsync(
            string corpusRootDirectory,
            IReadOnlyList<string> caseIds,
            string promptVersion,
            bool providerCallsAuthorized,
            BillStatementAiShadowReadinessPolicy readinessPolicy,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            corpusRootDirectory);

        ArgumentNullException.ThrowIfNull(
            caseIds);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            promptVersion);

        ArgumentNullException.ThrowIfNull(
            readinessPolicy);

        ValidateCaseIds(
            caseIds);

        /*
         * Provider spend must be explicitly authorized.
         *
         * Do this before reading the private corpus so a caller cannot use
         * this evaluator as an accidental corpus reader when provider
         * evaluation was not actually approved.
         */
        if (!providerCallsAuthorized)
        {
            throw new InvalidOperationException(
                "Offline AI provider evaluation requires explicit provider-call authorization.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        /*
         * Load and validate the entire selected population before the first
         * provider call.
         *
         * This prevents a malformed case discovered halfway through a run
         * from consuming provider spend for the cases that happened to come
         * before it.
         */
        var corpusCases =
            new List<BillStatementAiPrivateCorpusCase>(
                caseIds.Count);

        foreach (var caseId in
                 caseIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var corpusCase =
                await _loader.LoadAsync(
                    corpusRootDirectory,
                    caseId,
                    cancellationToken);

            corpusCases.Add(
                corpusCase);
        }

        var coverageSummary =
            CreateCoverageSummary(
                corpusCases);

        var coverageDecision =
            _coverageGate.Evaluate(
                coverageSummary,
                readinessPolicy);

        /*
         * Fail closed before any provider call.
         *
         * Coverage failure is a valid evaluation outcome, not an exception.
         * The caller receives aggregate-only reasons from the existing
         * coverage gate and can improve the corpus before spending money.
         */
        if (!coverageDecision
                .MayBeginOfflineProviderEvaluation)
        {
            return
                BillStatementAiPrivateCorpusProviderEvaluationResult
                    .CoverageRejected(
                        coverageSummary,
                        coverageDecision);
        }

        var observations =
            new List<BillStatementAiGroundTruthObservation>(
                corpusCases.Count);

        foreach (var corpusCase in
                 corpusCases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            BillStatementExtractionResult? extraction =
                null;

            var providerFailed =
                false;

            try
            {
                /*
                 * Do not provide the ground-truth provider identity as a hint.
                 *
                 * The purpose of this evaluation is to measure the extractor,
                 * not help it by leaking approved answers into its context.
                 */
                var candidate =
                    await _aiExtractor.ExtractAsync(
                        new BillStatementAiExtractionRequest(
                            DocumentText:
                                corpusCase.StatementText,

                            Hints:
                                new BillStatementExtractionHints(
                                    ExpectedProviderName:
                                        null,

                                    ExpectedCategory:
                                        null),

                            PromptVersion:
                                promptVersion),
                        cancellationToken);

                /*
                 * A provider response is still untrusted candidate data.
                 *
                 * It must pass BillWatch's existing deterministic evidence
                 * and candidate validation boundary before it may count as
                 * an extraction result in the evaluation.
                 */
                var conversion =
                    _conversionService.Convert(
                        corpusCase.StatementText,
                        candidate);

                if (conversion.IsAccepted)
                {
                    extraction =
                        conversion.Extraction;
                }

                /*
                 * A rejected candidate is intentionally not classified as a
                 * provider transport failure.
                 *
                 * The provider did respond; BillWatch rejected its candidate.
                 * That should reduce recall/readiness instead of disguising
                 * the trust-boundary rejection as network instability.
                 */
            }
            catch (BillStatementAiExtractionException)
            {
                /*
                 * Provider-specific failure details are intentionally dropped.
                 *
                 * They must not become part of corpus output, logs, or
                 * persistent evaluation data through this offline evaluator.
                 */
                providerFailed =
                    true;
            }

            observations.Add(
                new BillStatementAiGroundTruthObservation(
                    ProviderKey:
                        corpusCase.ProviderKey,

                    ExpectedStatement:
                        corpusCase.ExpectedStatement,

                    ExpectedLineItems:
                        corpusCase.ExpectedLineItems,

                    ProviderAttempted:
                        true,

                    ProviderFailed:
                        providerFailed,

                    ActualExtraction:
                        extraction,

                    /*
                     * False-alert measurement is deliberately not fabricated.
                     *
                     * A separate deterministic alert-evaluation checkpoint
                     * must establish this value before the overall shadow
                     * readiness gate can pass.
                     */
                    AlertEvaluated:
                        false,

                    FalseAlert:
                        false));
        }

        /*
         * Score while the sensitive observations are still in memory, then
         * return only aggregate counters.
         */
        var metrics =
            _groundTruthScorer.Score(
                observations);

        return
            BillStatementAiPrivateCorpusProviderEvaluationResult
                .Completed(
                    coverageSummary,
                    coverageDecision,
                    metrics);
    }

    private static void ValidateCaseIds(
        IReadOnlyList<string> caseIds)
    {
        if (caseIds.Count is <
                1 or >
                MaxCasesPerRun)
        {
            throw new ArgumentOutOfRangeException(
                nameof(caseIds),
                $"An offline provider evaluation requires between 1 and {MaxCasesPerRun} cases.");
        }

        if (caseIds.Any(
                string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Private corpus case identifiers cannot be empty.",
                nameof(caseIds));
        }

        if (caseIds.Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count() !=
            caseIds.Count)
        {
            throw new ArgumentException(
                "Private corpus case identifiers must be unique.",
                nameof(caseIds));
        }
    }

    private static BillStatementAiPrivateCorpusCatalogSummary
        CreateCoverageSummary(
            IReadOnlyList<BillStatementAiPrivateCorpusCase> corpusCases)
    {
        ArgumentNullException.ThrowIfNull(
            corpusCases);

        if (corpusCases.Count ==
            0)
        {
            throw new ArgumentException(
                "At least one validated private corpus case is required.",
                nameof(corpusCases));
        }

        var providerCounts =
            new Dictionary<string, long>(
                StringComparer.Ordinal);

        foreach (var corpusCase in
                 corpusCases)
        {
            ArgumentNullException.ThrowIfNull(
                corpusCase);

            var providerKey =
                corpusCase.ProviderKey
                    .Trim()
                    .ToUpperInvariant();

            providerCounts[providerKey] =
                providerCounts.GetValueOrDefault(
                    providerKey) +
                1;
        }

        return new BillStatementAiPrivateCorpusCatalogSummary(
            CaseCount:
                corpusCases.Count,

            DistinctProviderCount:
                providerCounts.Count,

            MinimumCasesForAnyProvider:
                providerCounts.Values.Min());
    }
}

/*
 * Aggregate-only result.
 *
 * Nothing here can expose statement content, ground truth, model output,
 * provider identity, evidence excerpts, local paths, or case identifiers.
 */
public sealed record BillStatementAiPrivateCorpusProviderEvaluationResult(
    bool ProviderEvaluationStarted,
    BillStatementAiPrivateCorpusCatalogSummary Coverage,
    BillStatementAiPrivateCorpusCoverageDecision CoverageDecision,
    BillStatementAiShadowReadinessMetrics? Metrics)
{
    public bool MayEnableRuntimeShadowMode =>
        false;

    public bool MayInfluencePersistence =>
        false;

    public static BillStatementAiPrivateCorpusProviderEvaluationResult
        CoverageRejected(
            BillStatementAiPrivateCorpusCatalogSummary coverage,
            BillStatementAiPrivateCorpusCoverageDecision coverageDecision)
    {
        ArgumentNullException.ThrowIfNull(
            coverage);

        ArgumentNullException.ThrowIfNull(
            coverageDecision);

        return new BillStatementAiPrivateCorpusProviderEvaluationResult(
            ProviderEvaluationStarted:
                false,

            Coverage:
                coverage,

            CoverageDecision:
                coverageDecision,

            Metrics:
                null);
    }

    public static BillStatementAiPrivateCorpusProviderEvaluationResult
        Completed(
            BillStatementAiPrivateCorpusCatalogSummary coverage,
            BillStatementAiPrivateCorpusCoverageDecision coverageDecision,
            BillStatementAiShadowReadinessMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(
            coverage);

        ArgumentNullException.ThrowIfNull(
            coverageDecision);

        ArgumentNullException.ThrowIfNull(
            metrics);

        if (!coverageDecision
                .MayBeginOfflineProviderEvaluation)
        {
            throw new ArgumentException(
                "A completed provider evaluation requires a passing corpus coverage decision.",
                nameof(coverageDecision));
        }

        return new BillStatementAiPrivateCorpusProviderEvaluationResult(
            ProviderEvaluationStarted:
                true,

            Coverage:
                coverage,

            CoverageDecision:
                coverageDecision,

            Metrics:
                metrics);
    }
}