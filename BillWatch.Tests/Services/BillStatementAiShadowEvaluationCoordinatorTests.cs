using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Statements;
using Microsoft.EntityFrameworkCore;

namespace BillWatch.Tests.Services;

public sealed class BillStatementAiShadowEvaluationCoordinatorTests
{
    [Fact]
    public async Task DurableClaim_IsWrittenBeforeOneProviderAttempt()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var uploadId =
            await AddUploadAsync(
                dbContext,
                userId);

        var aiExtractor =
            new RecordingAiExtractor(
                CompleteCandidate());

        var coordinator =
            CreateCoordinator(
                dbContext,
                aiExtractor);

        var first =
            await coordinator.EvaluateAsync(
                userId,
                uploadId,
                IncompleteRequest());

        var second =
            await coordinator.EvaluateAsync(
                userId,
                uploadId,
                IncompleteRequest());

        Assert.True(
            first.AiAttempted);

        Assert.True(
            first.AiCandidateAccepted);

        Assert.True(
            second.AiSuppressedByCostControl);

        Assert.Equal(
            1,
            aiExtractor.CallCount);

        var evaluation =
            await dbContext.BillStatementAiEvaluations
                .SingleAsync();

        Assert.Equal(
            1,
            evaluation.AttemptCount);

        Assert.Equal(
            BillStatementAiEvaluationStatus.AcceptedForShadowReview,
            evaluation.Status);

        Assert.True(
            evaluation.CandidateReadyForValidation);
    }

    [Fact]
    public async Task DeterministicComplete_SkipsLedgerAndProvider()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var uploadId =
            await AddUploadAsync(
                dbContext,
                userId);

        var aiExtractor =
            new RecordingAiExtractor(
                new InvalidOperationException(
                    "The provider must not be called."));

        var result =
            await CreateCoordinator(
                    dbContext,
                    aiExtractor)
                .EvaluateAsync(
                    userId,
                    uploadId,
                    new BillStatementExtractionRequest(
                        """
                        Total Due: $104.99
                        Billing Period: 08/01/2026 - 08/31/2026
                        """,
                        new BillStatementExtractionHints(
                            "ACME Fiber",
                            "Internet")));

        Assert.False(
            result.AiAttempted);

        Assert.Equal(
            0,
            aiExtractor.CallCount);

        Assert.Empty(
            dbContext.BillStatementAiEvaluations);
    }

    [Fact]
    public async Task CrossUserUpload_SuppressesProviderAndCreatesNoLedgerRecord()
    {
        await using var dbContext =
            CreateDbContext();

        var ownerUserId =
            Guid.NewGuid();

        var uploadId =
            await AddUploadAsync(
                dbContext,
                ownerUserId);

        var aiExtractor =
            new RecordingAiExtractor(
                CompleteCandidate());

        var result =
            await CreateCoordinator(
                    dbContext,
                    aiExtractor)
                .EvaluateAsync(
                    Guid.NewGuid(),
                    uploadId,
                    IncompleteRequest());

        Assert.True(
            result.AiSuppressedByCostControl);

        Assert.Equal(
            0,
            aiExtractor.CallCount);

        Assert.Empty(
            dbContext.BillStatementAiEvaluations);
    }

    [Fact]
    public async Task ProviderFailure_IsRecordedWithoutProviderDetails()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var uploadId =
            await AddUploadAsync(
                dbContext,
                userId);

        var sensitiveDetail =
            "sensitive-provider-detail";

        var result =
            await CreateCoordinator(
                    dbContext,
                    new RecordingAiExtractor(
                        new BillStatementAiExtractionException(
                            sensitiveDetail)))
                .EvaluateAsync(
                    userId,
                    uploadId,
                    IncompleteRequest());

        Assert.True(
            result.ProviderFailed);

        var evaluation =
            await dbContext.BillStatementAiEvaluations
                .SingleAsync();

        Assert.Equal(
            BillStatementAiEvaluationStatus.ProviderFailed,
            evaluation.Status);

        Assert.DoesNotContain(
            sensitiveDetail,
            string.Join(
                "|",
                typeof(
                        BillStatementAiEvaluationEntity)
                    .GetProperties()
                    .Select(
                        property =>
                            property.GetValue(
                                evaluation)?.ToString())),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationAfterDurableClaim_IsRecordedAndPropagated()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var uploadId =
            await AddUploadAsync(
                dbContext,
                userId);

        var aiExtractor =
            new BlockingAiExtractor();

        using var cancellationSource =
            new CancellationTokenSource();

        var evaluationTask =
            CreateCoordinator(
                    dbContext,
                    aiExtractor)
                .EvaluateAsync(
                    userId,
                    uploadId,
                    IncompleteRequest(),
                    cancellationSource.Token);

        await aiExtractor.Started;

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
                await evaluationTask);

        var evaluation =
            await dbContext.BillStatementAiEvaluations
                .SingleAsync();

        Assert.Equal(
            BillStatementAiEvaluationStatus.Canceled,
            evaluation.Status);

        Assert.Equal(
            1,
            evaluation.AttemptCount);

        Assert.NotNull(
            evaluation.CompletedAtUtc);
    }

    private static BillStatementAiShadowEvaluationCoordinator CreateCoordinator(
        BillWatchDbContext dbContext,
        IBillStatementAiExtractor aiExtractor)
    {
        var promptVersion =
            "bill-statement-extraction-v1";

        var shadowService =
            new BillStatementAiShadowEvaluationService(
                new DeterministicBillStatementExtractionService(
                    new DeterministicBillStatementParser(),
                    new DeterministicBillLineItemParser()),
                aiExtractor,
                new BillStatementAiCandidateConversionService(
                    new BillStatementAiCandidateValidator()),
                promptVersion);

        return new BillStatementAiShadowEvaluationCoordinator(
            shadowService,
            new BillStatementAiEvaluationLedger(
                dbContext,
                TimeProvider.System),
            new BillStatementAiProviderIdentity(
                "OpenAI",
                "gpt-4.1-mini",
                promptVersion));
    }

    private static BillStatementExtractionRequest IncompleteRequest()
    {
        return new BillStatementExtractionRequest(
            """
            ACME Fiber
            Statement period August 1, 2026 through August 31, 2026
            Amount payable $104.99
            USD
            """,
            new BillStatementExtractionHints(
                "ACME Fiber",
                "Internet"));
    }

    private static BillStatementAiCandidate CompleteCandidate()
    {
        const string periodEvidence =
            "Statement period August 1, 2026 through August 31, 2026";

        return new BillStatementAiCandidate(
            ProviderName:
                null,
            AccountIdentifierSuffix:
                null,
            BillingPeriodStart:
                new DateOnly(
                    2026,
                    8,
                    1),
            BillingPeriodEnd:
                new DateOnly(
                    2026,
                    8,
                    31),
            StatementDate:
                null,
            DueDate:
                null,
            PreviousBalance:
                null,
            Payments:
                null,
            CurrentCharges:
                null,
            TotalDue:
                104.99m,
            CurrencyCode:
                "USD",
            PlanOrService:
                null,
            UsageSummary:
                null,
            LineItems:
                [],
            Evidence:
                [
                    new BillStatementAiEvidence(
                        BillStatementAiFactKeys.BillingPeriodStart,
                        periodEvidence),
                    new BillStatementAiEvidence(
                        BillStatementAiFactKeys.BillingPeriodEnd,
                        periodEvidence),
                    new BillStatementAiEvidence(
                        BillStatementAiFactKeys.TotalDue,
                        "Amount payable $104.99"),
                    new BillStatementAiEvidence(
                        BillStatementAiFactKeys.CurrencyCode,
                        "USD")
                ],
            ModelConfidence:
                BillStatementAiModelConfidence.High);
    }

    private static BillWatchDbContext CreateDbContext()
    {
        return new BillWatchDbContext(
            new DbContextOptionsBuilder<BillWatchDbContext>()
                .UseInMemoryDatabase(
                    $"ai-shadow-coordinator-{Guid.NewGuid():N}")
                .Options);
    }

    private static async Task<Guid> AddUploadAsync(
        BillWatchDbContext dbContext,
        Guid userId)
    {
        var upload =
            new BillStatementUploadEntity
            {
                UserId =
                    userId,
                BillStreamId =
                    Guid.NewGuid(),
                StorageKey =
                    $"test/{Guid.NewGuid():N}",
                MediaType =
                    "application/pdf",
                FileExtension =
                    ".pdf",
                SizeBytes =
                    100
            };

        dbContext.BillStatementUploads.Add(
            upload);

        await dbContext.SaveChangesAsync();

        return upload.Id;
    }

    private sealed class RecordingAiExtractor
        : IBillStatementAiExtractor
    {
        private readonly BillStatementAiCandidate? _candidate;

        private readonly Exception? _exception;

        public RecordingAiExtractor(
            BillStatementAiCandidate candidate)
        {
            _candidate =
                candidate;
        }

        public RecordingAiExtractor(
            Exception exception)
        {
            _exception =
                exception;
        }

        public int CallCount { get; private set; }

        public Task<BillStatementAiCandidate> ExtractAsync(
            BillStatementAiExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;

            if (_exception is not null)
            {
                return Task.FromException<BillStatementAiCandidate>(
                    _exception);
            }

            return Task.FromResult(
                _candidate!);
        }
    }

    private sealed class BlockingAiExtractor
        : IBillStatementAiExtractor
    {
        private readonly TaskCompletionSource _started =
            new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started =>
            _started.Task;

        public async Task<BillStatementAiCandidate> ExtractAsync(
            BillStatementAiExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();

            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);

            throw new InvalidOperationException(
                "The blocking extractor should only end by cancellation.");
        }
    }
}
