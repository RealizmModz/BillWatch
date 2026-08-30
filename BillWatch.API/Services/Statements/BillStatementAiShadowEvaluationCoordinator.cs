using BillWatch.API.Data.Entities;

namespace BillWatch.API.Services.Statements;

/*
 * Composes deterministic-first shadow evaluation with the durable attempt
 * ledger. This class is intentionally not registered in Program yet.
 */
public sealed class BillStatementAiShadowEvaluationCoordinator
{
    private readonly BillStatementAiShadowEvaluationService
        _shadowEvaluationService;

    private readonly BillStatementAiEvaluationLedger
        _evaluationLedger;

    private readonly BillStatementAiProviderIdentity
        _providerIdentity;

    private readonly BillStatementAiShadowActivationPolicy
        _activationPolicy;

    public BillStatementAiShadowEvaluationCoordinator(
        BillStatementAiShadowEvaluationService shadowEvaluationService,
        BillStatementAiEvaluationLedger evaluationLedger,
        BillStatementAiProviderIdentity providerIdentity,
        BillStatementAiShadowActivationPolicy activationPolicy)
    {
        ArgumentNullException.ThrowIfNull(
            shadowEvaluationService);

        ArgumentNullException.ThrowIfNull(
            evaluationLedger);

        ArgumentNullException.ThrowIfNull(
            providerIdentity);

        ArgumentNullException.ThrowIfNull(
            activationPolicy);

        _shadowEvaluationService =
            shadowEvaluationService;

        _evaluationLedger =
            evaluationLedger;

        _providerIdentity =
            providerIdentity;

        _activationPolicy =
            activationPolicy;
    }

    public async Task<BillStatementAiShadowEvaluationResult> EvaluateAsync(
        Guid userId,
        Guid billStatementUploadId,
        BillStatementExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        BillStatementAiEvaluationStartResult? startResult =
            null;

        BillStatementAiShadowEvaluationResult result;

        try
        {
            result =
                await _shadowEvaluationService.EvaluateAsync(
                    request,
                    async gateCancellationToken =>
                    {
                        var activation =
                            _activationPolicy.Evaluate(
                                _providerIdentity.Enabled);

                        if (!activation.MayAttemptProvider)
                        {
                            return false;
                        }

                        startResult =
                            await _evaluationLedger.TryBeginAsync(
                                userId,
                                billStatementUploadId,
                                _providerIdentity.Provider,
                                _providerIdentity.Model,
                                _providerIdentity.PromptVersion,
                                gateCancellationToken);

                        return startResult.Outcome ==
                            BillStatementAiEvaluationStartOutcome.Started;
                    },
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested &&
                startResult?.Outcome is
                    BillStatementAiEvaluationStartOutcome.Started &&
                startResult.EvaluationId.HasValue)
        {
            /*
             * The provider may already have received the request. Preserve
             * the at-most-once cost boundary by consuming the durable claim
             * and recording cancellation before propagating it.
             */
            await _evaluationLedger.CompleteAsync(
                userId,
                startResult.EvaluationId.Value,
                BillStatementAiEvaluationStatus.Canceled,
                candidateReadyForValidation:
                    false,
                CancellationToken.None);

            throw;
        }

        if (startResult?.Outcome is not
            BillStatementAiEvaluationStartOutcome.Started)
        {
            return result;
        }

        var evaluationId =
            startResult.EvaluationId
            ?? throw new InvalidOperationException(
                "A started AI evaluation did not return an identifier.");

        var status =
            DetermineTerminalStatus(
                result);

        var completed =
            await _evaluationLedger.CompleteAsync(
                userId,
                evaluationId,
                status,
                result.AiCandidateReadyForValidation,
                cancellationToken);

        if (!completed)
        {
            throw new InvalidOperationException(
                "The owned AI evaluation could not be completed.");
        }

        return result;
    }

    private static BillStatementAiEvaluationStatus DetermineTerminalStatus(
        BillStatementAiShadowEvaluationResult result)
    {
        if (result.ProviderFailed)
        {
            return BillStatementAiEvaluationStatus.ProviderFailed;
        }

        return result.AiCandidateAccepted
            ? BillStatementAiEvaluationStatus.AcceptedForShadowReview
            : BillStatementAiEvaluationStatus.Rejected;
    }
}

public sealed record BillStatementAiProviderIdentity(
    string Provider,
    string Model,
    string PromptVersion,
    bool Enabled);
