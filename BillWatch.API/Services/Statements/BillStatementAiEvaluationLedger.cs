using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.API.Services.Statements;

/*
 * Restart-safe cost-control ledger.
 *
 * This service stores attempt metadata only. It never receives or persists
 * statement text, prompts, candidates, evidence, or provider error bodies.
 */
public sealed class BillStatementAiEvaluationLedger
{
    private const int MaxProviderLength =
        50;

    private const int MaxModelLength =
        100;

    private const int MaxPromptVersionLength =
        100;

    private readonly BillWatchDbContext _dbContext;

    private readonly TimeProvider _timeProvider;

    public BillStatementAiEvaluationLedger(
        BillWatchDbContext dbContext,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(
            dbContext);

        ArgumentNullException.ThrowIfNull(
            timeProvider);

        _dbContext =
            dbContext;

        _timeProvider =
            timeProvider;
    }

    public async Task<BillStatementAiEvaluationStartResult> TryBeginAsync(
        Guid userId,
        Guid billStatementUploadId,
        string provider,
        string model,
        string promptVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateId(
            userId,
            nameof(userId));

        ValidateId(
            billStatementUploadId,
            nameof(billStatementUploadId));

        provider =
            ValidateIdentifier(
                provider,
                nameof(provider),
                MaxProviderLength);

        model =
            ValidateIdentifier(
                model,
                nameof(model),
                MaxModelLength);

        promptVersion =
            ValidateIdentifier(
                promptVersion,
                nameof(promptVersion),
                MaxPromptVersionLength);

        var uploadExists =
            await _dbContext.BillStatementUploads
                .AsNoTracking()
                .AnyAsync(
                    upload =>
                        upload.Id ==
                            billStatementUploadId &&
                        upload.UserId ==
                            userId,
                    cancellationToken);

        if (!uploadExists)
        {
            return BillStatementAiEvaluationStartResult.UploadNotFound();
        }

        var existing =
            await FindExistingAsync(
                userId,
                billStatementUploadId,
                provider,
                model,
                promptVersion,
                cancellationToken);

        if (existing is not null)
        {
            return BillStatementAiEvaluationStartResult.AlreadyExists(
                existing.Id,
                existing.Status);
        }

        var now =
            _timeProvider.GetUtcNow();

        var evaluation =
            new BillStatementAiEvaluationEntity
            {
                UserId =
                    userId,

                BillStatementUploadId =
                    billStatementUploadId,

                Provider =
                    provider,

                Model =
                    model,

                PromptVersion =
                    promptVersion,

                Status =
                    BillStatementAiEvaluationStatus.InProgress,

                AttemptCount =
                    1,

                LastAttemptedAtUtc =
                    now,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };

        _dbContext.BillStatementAiEvaluations.Add(
            evaluation);

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return BillStatementAiEvaluationStartResult.Started(
                evaluation.Id);
        }
        catch (DbUpdateException)
        {
            /*
             * A concurrent worker may have won the unique cost key.
             * Confirm that exact ownership-scoped record exists before
             * treating the write failure as an ordinary duplicate.
             */
            _dbContext.Entry(
                    evaluation)
                .State =
                EntityState.Detached;

            existing =
                await FindExistingAsync(
                    userId,
                    billStatementUploadId,
                    provider,
                    model,
                    promptVersion,
                    cancellationToken);

            if (existing is null)
            {
                throw;
            }

            return BillStatementAiEvaluationStartResult.AlreadyExists(
                existing.Id,
                existing.Status);
        }
    }

    public async Task<bool> CompleteAsync(
        Guid userId,
        Guid evaluationId,
        BillStatementAiEvaluationStatus status,
        bool candidateReadyForValidation,
        CancellationToken cancellationToken = default)
    {
        ValidateId(
            userId,
            nameof(userId));

        ValidateId(
            evaluationId,
            nameof(evaluationId));

        ValidateTerminalStatus(
            status);

        var evaluation =
            await _dbContext.BillStatementAiEvaluations
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                            evaluationId &&
                        item.UserId ==
                            userId,
                    cancellationToken);

        if (evaluation is null ||
            evaluation.Status !=
                BillStatementAiEvaluationStatus.InProgress)
        {
            return false;
        }

        var now =
            _timeProvider.GetUtcNow();

        evaluation.Status =
            status;

        evaluation.CandidateReadyForValidation =
            candidateReadyForValidation;

        evaluation.CompletedAtUtc =
            now;

        evaluation.UpdatedAtUtc =
            now;

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    private async Task<BillStatementAiEvaluationEntity?> FindExistingAsync(
        Guid userId,
        Guid billStatementUploadId,
        string provider,
        string model,
        string promptVersion,
        CancellationToken cancellationToken)
    {
        return await _dbContext.BillStatementAiEvaluations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                evaluation =>
                    evaluation.UserId ==
                        userId &&
                    evaluation.BillStatementUploadId ==
                        billStatementUploadId &&
                    evaluation.Provider ==
                        provider &&
                    evaluation.Model ==
                        model &&
                    evaluation.PromptVersion ==
                        promptVersion,
                cancellationToken);
    }

    private static void ValidateId(
        Guid value,
        string parameterName)
    {
        if (value ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "A non-empty identifier is required.",
                parameterName);
        }
    }

    private static string ValidateIdentifier(
        string value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                "A value is required.",
                parameterName);
        }

        value =
            value.Trim();

        if (value.Length >
            maximumLength)
        {
            throw new ArgumentException(
                $"The value exceeds the maximum length of {maximumLength} characters.",
                parameterName);
        }

        return value;
    }

    private static void ValidateTerminalStatus(
        BillStatementAiEvaluationStatus status)
    {
        if (status is
            BillStatementAiEvaluationStatus.AcceptedForShadowReview or
            BillStatementAiEvaluationStatus.Rejected or
            BillStatementAiEvaluationStatus.ProviderFailed or
            BillStatementAiEvaluationStatus.SkippedDeterministicComplete or
            BillStatementAiEvaluationStatus.Canceled)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(status),
            "A terminal AI evaluation status is required.");
    }
}

public sealed record BillStatementAiEvaluationStartResult(
    BillStatementAiEvaluationStartOutcome Outcome,
    Guid? EvaluationId,
    BillStatementAiEvaluationStatus? ExistingStatus)
{
    public static BillStatementAiEvaluationStartResult Started(
        Guid evaluationId)
    {
        return new BillStatementAiEvaluationStartResult(
            BillStatementAiEvaluationStartOutcome.Started,
            evaluationId,
            ExistingStatus:
                null);
    }

    public static BillStatementAiEvaluationStartResult AlreadyExists(
        Guid evaluationId,
        BillStatementAiEvaluationStatus status)
    {
        return new BillStatementAiEvaluationStartResult(
            BillStatementAiEvaluationStartOutcome.AlreadyExists,
            evaluationId,
            status);
    }

    public static BillStatementAiEvaluationStartResult UploadNotFound()
    {
        return new BillStatementAiEvaluationStartResult(
            BillStatementAiEvaluationStartOutcome.UploadNotFound,
            EvaluationId:
                null,
            ExistingStatus:
                null);
    }
}

public enum BillStatementAiEvaluationStartOutcome
{
    Started = 0,
    AlreadyExists = 1,
    UploadNotFound = 2
}
