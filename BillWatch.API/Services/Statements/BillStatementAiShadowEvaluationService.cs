namespace BillWatch.API.Services.Statements;

/*
 * Evaluates AI-assisted extraction without exposing AI facts to the
 * statement-processing or persistence pipeline.
 *
 * This deliberately does not implement IBillStatementExtractionService.
 * It is a shadow evaluator, not a production extraction strategy.
 */
public sealed class BillStatementAiShadowEvaluationService
{
    private readonly DeterministicBillStatementExtractionService
        _deterministicExtractor;

    private readonly IBillStatementAiExtractor
        _aiExtractor;

    private readonly BillStatementAiCandidateConversionService
        _candidateConversionService;

    private readonly string _promptVersion;

    public BillStatementAiShadowEvaluationService(
        DeterministicBillStatementExtractionService deterministicExtractor,
        IBillStatementAiExtractor aiExtractor,
        BillStatementAiCandidateConversionService candidateConversionService,
        string promptVersion)
    {
        ArgumentNullException.ThrowIfNull(
            deterministicExtractor);

        ArgumentNullException.ThrowIfNull(
            aiExtractor);

        ArgumentNullException.ThrowIfNull(
            candidateConversionService);

        if (string.IsNullOrWhiteSpace(
                promptVersion))
        {
            throw new ArgumentException(
                "A prompt version is required.",
                nameof(promptVersion));
        }

        _deterministicExtractor =
            deterministicExtractor;

        _aiExtractor =
            aiExtractor;

        _candidateConversionService =
            candidateConversionService;

        _promptVersion =
            promptVersion.Trim();
    }

    public async Task<BillStatementAiShadowEvaluationResult> EvaluateAsync(
        BillStatementExtractionRequest request,
        BillStatementAiAttemptGate tryAcquireAiAttempt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        ArgumentNullException.ThrowIfNull(
            request.DocumentText);

        ArgumentNullException.ThrowIfNull(
            tryAcquireAiAttempt);

        cancellationToken.ThrowIfCancellationRequested();

        var deterministicResult =
            await _deterministicExtractor.ExtractAsync(
                request,
                cancellationToken);

        /*
         * Cost control: do not call a model when deterministic extraction
         * already produced every fact required for validation.
         */
        if (deterministicResult.IsReadyForValidation)
        {
            return BillStatementAiShadowEvaluationResult.NotAttempted(
                deterministicResult);
        }

        var mayAttemptAi =
            await tryAcquireAiAttempt(
                cancellationToken);

        if (!mayAttemptAi)
        {
            return BillStatementAiShadowEvaluationResult.SuppressedByCostControl(
                deterministicResult);
        }

        BillStatementAiCandidate candidate;

        try
        {
            /*
             * Exactly one provider attempt is made per evaluation.
             * Provider retry policy is intentionally not added here.
             */
            candidate =
                await _aiExtractor.ExtractAsync(
                    new BillStatementAiExtractionRequest(
                        DocumentText:
                            request.DocumentText,

                        Hints:
                            request.Hints,

                        PromptVersion:
                            _promptVersion),
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BillStatementAiExtractionException)
        {
            return BillStatementAiShadowEvaluationResult.FromProviderFailure(
                deterministicResult);
        }

        var conversion =
            _candidateConversionService.Convert(
                request.DocumentText,
                candidate);

        if (!conversion.IsAccepted ||
            conversion.Extraction is null)
        {
            return BillStatementAiShadowEvaluationResult.Rejected(
                deterministicResult,
                conversion.Errors);
        }

        var conflicts =
            FindDeterministicConflicts(
                deterministicResult,
                conversion.Extraction);

        if (conflicts.Count >
            0)
        {
            return BillStatementAiShadowEvaluationResult.Rejected(
                deterministicResult,
                conflicts);
        }

        return BillStatementAiShadowEvaluationResult.AcceptedForShadowReview(
            deterministicResult,
            conversion.Extraction.IsReadyForValidation);
    }

    private static IReadOnlyList<string> FindDeterministicConflicts(
        BillStatementExtractionResult deterministicResult,
        BillStatementExtractionResult aiResult)
    {
        var conflicts =
            new List<string>();

        AddConflictIfDifferent(
            deterministicResult.Statement.TotalAmount,
            aiResult.Statement.TotalAmount,
            nameof(
                BillStatementStructuredData.TotalAmount),
            conflicts);

        AddConflictIfDifferent(
            deterministicResult.Statement.BillingPeriodStart,
            aiResult.Statement.BillingPeriodStart,
            nameof(
                BillStatementStructuredData.BillingPeriodStart),
            conflicts);

        AddConflictIfDifferent(
            deterministicResult.Statement.BillingPeriodEnd,
            aiResult.Statement.BillingPeriodEnd,
            nameof(
                BillStatementStructuredData.BillingPeriodEnd),
            conflicts);

        AddConflictIfDifferent(
            deterministicResult.Statement.StatementDate,
            aiResult.Statement.StatementDate,
            nameof(
                BillStatementStructuredData.StatementDate),
            conflicts);

        AddConflictIfDifferent(
            deterministicResult.Statement.DueDate,
            aiResult.Statement.DueDate,
            nameof(
                BillStatementStructuredData.DueDate),
            conflicts);

        if (!string.IsNullOrWhiteSpace(
                deterministicResult.Statement.CurrencyCode) &&
            !string.IsNullOrWhiteSpace(
                aiResult.Statement.CurrencyCode) &&
            !string.Equals(
                deterministicResult.Statement.CurrencyCode,
                aiResult.Statement.CurrencyCode,
                StringComparison.OrdinalIgnoreCase))
        {
            conflicts.Add(
                $"AI output conflicts with deterministic '{nameof(BillStatementStructuredData.CurrencyCode)}'.");
        }

        return conflicts.AsReadOnly();
    }

    private static void AddConflictIfDifferent<T>(
        T? deterministicValue,
        T? aiValue,
        string fieldName,
        ICollection<string> conflicts)
        where T : struct
    {
        if (!deterministicValue.HasValue ||
            !aiValue.HasValue ||
            EqualityComparer<T>.Default.Equals(
                deterministicValue.Value,
                aiValue.Value))
        {
            return;
        }

        conflicts.Add(
            $"AI output conflicts with deterministic '{fieldName}'.");
    }
}

public sealed record BillStatementAiShadowEvaluationResult(
    BillStatementExtractionResult DeterministicExtraction,
    bool AiAttempted,
    bool AiSuppressedByCostControl,
    bool AiCandidateAccepted,
    bool AiCandidateReadyForValidation,
    bool ProviderFailed,
    IReadOnlyList<string> RejectionReasons)
{
    public static BillStatementAiShadowEvaluationResult NotAttempted(
        BillStatementExtractionResult deterministicExtraction)
    {
        return new BillStatementAiShadowEvaluationResult(
            deterministicExtraction,
            AiAttempted:
                false,
            AiSuppressedByCostControl:
                false,
            AiCandidateAccepted:
                false,
            AiCandidateReadyForValidation:
                false,
            ProviderFailed:
                false,
            RejectionReasons:
                []);
    }

    public static BillStatementAiShadowEvaluationResult SuppressedByCostControl(
        BillStatementExtractionResult deterministicExtraction)
    {
        return new BillStatementAiShadowEvaluationResult(
            deterministicExtraction,
            AiAttempted:
                false,
            AiSuppressedByCostControl:
                true,
            AiCandidateAccepted:
                false,
            AiCandidateReadyForValidation:
                false,
            ProviderFailed:
                false,
            RejectionReasons:
                []);
    }

    public static BillStatementAiShadowEvaluationResult FromProviderFailure(
        BillStatementExtractionResult deterministicExtraction)
    {
        return new BillStatementAiShadowEvaluationResult(
            deterministicExtraction,
            AiAttempted:
                true,
            AiSuppressedByCostControl:
                false,
            AiCandidateAccepted:
                false,
            AiCandidateReadyForValidation:
                false,
            ProviderFailed:
                true,
            RejectionReasons:
                []);
    }

    public static BillStatementAiShadowEvaluationResult Rejected(
        BillStatementExtractionResult deterministicExtraction,
        IReadOnlyList<string> rejectionReasons)
    {
        ArgumentNullException.ThrowIfNull(
            rejectionReasons);

        return new BillStatementAiShadowEvaluationResult(
            deterministicExtraction,
            AiAttempted:
                true,
            AiSuppressedByCostControl:
                false,
            AiCandidateAccepted:
                false,
            AiCandidateReadyForValidation:
                false,
            ProviderFailed:
                false,
            RejectionReasons:
                rejectionReasons);
    }

    public static BillStatementAiShadowEvaluationResult AcceptedForShadowReview(
        BillStatementExtractionResult deterministicExtraction,
        bool isReadyForValidation)
    {
        return new BillStatementAiShadowEvaluationResult(
            deterministicExtraction,
            AiAttempted:
                true,
            AiSuppressedByCostControl:
                false,
            AiCandidateAccepted:
                true,
            AiCandidateReadyForValidation:
                isReadyForValidation,
            ProviderFailed:
                false,
            RejectionReasons:
                []);
    }
}

public delegate Task<bool> BillStatementAiAttemptGate(
    CancellationToken cancellationToken);
