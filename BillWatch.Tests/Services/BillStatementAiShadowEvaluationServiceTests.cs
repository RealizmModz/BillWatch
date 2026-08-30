using BillWatch.API.Services.Statements;

namespace BillWatch.Tests.Services;

public sealed class BillStatementAiShadowEvaluationServiceTests
{
    private const string PromptVersion =
        "bill-statement-extraction-v1";

    [Fact]
    public async Task CompleteDeterministicResult_DoesNotCallAi()
    {
        const string documentText =
            """
            Total Due: $104.99
            Billing Period: 08/01/2026 - 08/31/2026
            """;

        var aiExtractor =
            new RecordingAiExtractor(
                EmptyCandidate());

        var result =
            await CreateService(
                    aiExtractor)
                .EvaluateAsync(
                    CreateRequest(
                        documentText),
                    AllowAttempt);

        Assert.False(
            result.AiAttempted);

        Assert.Equal(
            0,
            aiExtractor.CallCount);

        Assert.True(
            result.DeterministicExtraction.IsReadyForValidation);
    }

    [Fact]
    public async Task IncompleteDeterministicResult_MakesOneShadowAiAttempt()
    {
        const string documentText =
            """
            ACME Fiber
            Statement period August 1, 2026 through August 31, 2026
            Amount payable $104.99
            USD
            """;

        var aiExtractor =
            new RecordingAiExtractor(
                CompleteCandidate(
                    totalDue:
                        104.99m));

        var result =
            await CreateService(
                    aiExtractor)
                .EvaluateAsync(
                    CreateRequest(
                        documentText),
                    AllowAttempt);

        Assert.True(
            result.AiAttempted);

        Assert.True(
            result.AiCandidateAccepted);

        Assert.True(
            result.AiCandidateReadyForValidation);

        Assert.Equal(
            1,
            aiExtractor.CallCount);

        Assert.False(
            result.DeterministicExtraction.IsReadyForValidation);

        Assert.Equal(
            BillStatementExtractionSource.Deterministic,
            result.DeterministicExtraction.Source);

        Assert.Equal(
            PromptVersion,
            aiExtractor.LastRequest?.PromptVersion);
    }

    [Fact]
    public async Task ClaimedValueMissingFromRealExcerpt_IsRejected()
    {
        const string documentText =
            """
            ACME Fiber
            Statement period August 1, 2026 through August 31, 2026
            Amount payable $104.99
            USD
            """;

        var candidate =
            CompleteCandidate(
                totalDue:
                    999.99m);

        var result =
            await CreateService(
                    new RecordingAiExtractor(
                        candidate))
                .EvaluateAsync(
                    CreateRequest(
                        documentText),
                    AllowAttempt);

        Assert.True(
            result.AiAttempted);

        Assert.False(
            result.AiCandidateAccepted);

        Assert.Contains(
            result.RejectionReasons,
            reason =>
                reason.Contains(
                    "does not contain the extracted value",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProviderFailure_IsSanitizedAndNotRetried()
    {
        var aiExtractor =
            new RecordingAiExtractor(
                new BillStatementAiExtractionException(
                    "Provider detail that must not escape."));

        var result =
            await CreateService(
                    aiExtractor)
                .EvaluateAsync(
                    CreateRequest(
                        "Incomplete statement"),
                    AllowAttempt);

        Assert.True(
            result.AiAttempted);

        Assert.True(
            result.ProviderFailed);

        Assert.False(
            result.AiCandidateAccepted);

        Assert.Empty(
            result.RejectionReasons);

        Assert.Equal(
            1,
            aiExtractor.CallCount);
    }

    [Fact]
    public async Task ConflictWithDeterministicFact_IsRejected()
    {
        const string documentText =
            """
            ACME Fiber
            Total Due: $104.99
            Statement period August 1, 2026 through August 31, 2026
            Other amount $99.99
            USD
            """;

        var candidate =
            CompleteCandidate(
                totalDue:
                    99.99m,
                totalEvidence:
                    "Other amount $99.99");

        var result =
            await CreateService(
                    new RecordingAiExtractor(
                        candidate))
                .EvaluateAsync(
                    CreateRequest(
                        documentText),
                    AllowAttempt);

        Assert.True(
            result.AiAttempted);

        Assert.False(
            result.AiCandidateAccepted);

        Assert.Contains(
            result.RejectionReasons,
            reason =>
                reason.Contains(
                    nameof(
                        BillStatementStructuredData.TotalAmount),
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancellationDuringAiCall_Propagates()
    {
        var aiExtractor =
            new BlockingAiExtractor();

        using var cancellationSource =
            new CancellationTokenSource();

        var evaluationTask =
            CreateService(
                    aiExtractor)
                .EvaluateAsync(
                    CreateRequest(
                        "Incomplete statement"),
                    AllowAttempt,
                    cancellationSource.Token);

        await aiExtractor.Started;

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
                await evaluationTask);

        Assert.Equal(
            1,
            aiExtractor.CallCount);
    }

    [Fact]
    public async Task DeniedAttemptGate_SuppressesProviderCall()
    {
        var aiExtractor =
            new RecordingAiExtractor(
                EmptyCandidate());

        var result =
            await CreateService(
                    aiExtractor)
                .EvaluateAsync(
                    CreateRequest(
                        "Incomplete statement"),
                    _ =>
                        Task.FromResult(
                            false));

        Assert.True(
            result.AiSuppressedByCostControl);

        Assert.False(
            result.AiAttempted);

        Assert.Equal(
            0,
            aiExtractor.CallCount);
    }

    private static Task<bool> AllowAttempt(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            true);
    }

    private static BillStatementAiShadowEvaluationService CreateService(
        IBillStatementAiExtractor aiExtractor)
    {
        return new BillStatementAiShadowEvaluationService(
            new DeterministicBillStatementExtractionService(
                new DeterministicBillStatementParser(),
                new DeterministicBillLineItemParser()),
            aiExtractor,
            new BillStatementAiCandidateConversionService(
                new BillStatementAiCandidateValidator()),
            PromptVersion);
    }

    private static BillStatementExtractionRequest CreateRequest(
        string documentText)
    {
        return new BillStatementExtractionRequest(
            documentText,
            new BillStatementExtractionHints(
                ExpectedProviderName:
                    "ACME Fiber",
                ExpectedCategory:
                    "Internet"));
    }

    private static BillStatementAiCandidate CompleteCandidate(
        decimal totalDue,
        string totalEvidence = "Amount payable $104.99")
    {
        const string periodEvidence =
            "Statement period August 1, 2026 through August 31, 2026";

        return EmptyCandidate() with
        {
            BillingPeriodStart =
                new DateOnly(
                    2026,
                    8,
                    1),

            BillingPeriodEnd =
                new DateOnly(
                    2026,
                    8,
                    31),

            TotalDue =
                totalDue,

            CurrencyCode =
                "USD",

            Evidence =
                [
                    new BillStatementAiEvidence(
                        BillStatementAiFactKeys.BillingPeriodStart,
                        periodEvidence),

                    new BillStatementAiEvidence(
                        BillStatementAiFactKeys.BillingPeriodEnd,
                        periodEvidence),

                    new BillStatementAiEvidence(
                        BillStatementAiFactKeys.TotalDue,
                        totalEvidence),

                    new BillStatementAiEvidence(
                        BillStatementAiFactKeys.CurrencyCode,
                        "USD")
                ]
        };
    }

    private static BillStatementAiCandidate EmptyCandidate()
    {
        return new BillStatementAiCandidate(
            ProviderName:
                null,
            AccountIdentifierSuffix:
                null,
            BillingPeriodStart:
                null,
            BillingPeriodEnd:
                null,
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
                null,
            CurrencyCode:
                null,
            PlanOrService:
                null,
            UsageSummary:
                null,
            LineItems:
                [],
            Evidence:
                [],
            ModelConfidence:
                BillStatementAiModelConfidence.Unknown);
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

        public BillStatementAiExtractionRequest? LastRequest
        {
            get;
            private set;
        }

        public Task<BillStatementAiCandidate> ExtractAsync(
            BillStatementAiExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            LastRequest =
                request;

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

        public int CallCount { get; private set; }

        public async Task<BillStatementAiCandidate> ExtractAsync(
            BillStatementAiExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            _started.TrySetResult();

            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);

            throw new InvalidOperationException(
                "The blocking extractor should only end by cancellation.");
        }
    }
}
