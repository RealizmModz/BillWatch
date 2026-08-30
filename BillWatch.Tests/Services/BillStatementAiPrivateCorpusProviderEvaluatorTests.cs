using BillWatch.API.Services.Statements;
using System.Globalization;

namespace BillWatch.Tests.Services;

public sealed class BillStatementAiPrivateCorpusProviderEvaluatorTests
{
    [Fact]
    public async Task Evaluate_RequiresExplicitProviderAuthorizationBeforeCorpusRead()
    {
        var extractor =
            new FakeAiExtractor();

        var evaluator =
            CreateEvaluator(
                extractor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                evaluator.EvaluateAsync(
                    corpusRootDirectory:
                        Path.Combine(
                            Path.GetTempPath(),
                            $"missing-billwatch-corpus-{Guid.NewGuid():N}"),
                    caseIds:
                        [
                            "case-001",
                            "case-002"
                        ],
                    promptVersion:
                        "offline-test-v1",
                    providerCallsAuthorized:
                        false,
                    readinessPolicy:
                        CreateReadinessPolicy()));

        Assert.Equal(
            0,
            extractor.CallCount);
    }

    [Fact]
    public async Task Evaluate_RejectsInsufficientCoverageBeforeProviderCall()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            caseId:
                "provider-a-001",
            providerKey:
                "provider-a",
            totalAmount:
                104.99m);

        directory.WriteCase(
            caseId:
                "provider-a-002",
            providerKey:
                "provider-a",
            totalAmount:
                55m);

        var extractor =
            new FakeAiExtractor();

        var result =
            await CreateEvaluator(
                    extractor)
                .EvaluateAsync(
                    directory.Path,
                    [
                        "provider-a-001",
                        "provider-a-002"
                    ],
                    promptVersion:
                        "offline-test-v1",
                    providerCallsAuthorized:
                        true,
                    readinessPolicy:
                        CreateReadinessPolicy());

        Assert.False(
            result.ProviderEvaluationStarted);

        Assert.Null(
            result.Metrics);

        Assert.False(
            result.CoverageDecision
                .MayBeginOfflineProviderEvaluation);

        Assert.NotEmpty(
            result.CoverageDecision.Failures);

        Assert.Equal(
            0,
            extractor.CallCount);

        Assert.False(
            result.MayEnableRuntimeShadowMode);

        Assert.False(
            result.MayInfluencePersistence);
    }

    [Fact]
    public async Task Evaluate_ProducesAggregateMetricsFromValidatedCandidates()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            caseId:
                "provider-a-001",
            providerKey:
                "provider-a",
            totalAmount:
                104.99m);

        directory.WriteCase(
            caseId:
                "provider-b-001",
            providerKey:
                "provider-b",
            totalAmount:
                55m);

        var extractor =
            new FakeAiExtractor();

        var result =
            await CreateEvaluator(
                    extractor)
                .EvaluateAsync(
                    directory.Path,
                    [
                        "provider-a-001",
                        "provider-b-001"
                    ],
                    promptVersion:
                        "offline-test-v1",
                    providerCallsAuthorized:
                        true,
                    readinessPolicy:
                        CreateReadinessPolicy());

        Assert.True(
            result.ProviderEvaluationStarted);

        Assert.True(
            result.CoverageDecision
                .MayBeginOfflineProviderEvaluation);

        Assert.Equal(
            2,
            result.Coverage.CaseCount);

        Assert.Equal(
            2,
            result.Coverage.DistinctProviderCount);

        Assert.Equal(
            1,
            result.Coverage.MinimumCasesForAnyProvider);

        var metrics =
            Assert.IsType<BillStatementAiShadowReadinessMetrics>(
                result.Metrics);

        Assert.Equal(
            2,
            metrics.EvaluatedStatementCount);

        Assert.Equal(
            2,
            metrics.DistinctProviderCount);

        Assert.Equal(
            1,
            metrics.MinimumStatementsForAnyProvider);

        Assert.Equal(
            2,
            metrics.ProviderAttemptCount);

        Assert.Equal(
            0,
            metrics.ProviderFailureCount);

        Assert.Equal(
            2,
            metrics.ReadyCandidateStatementCount);

        Assert.Equal(
            8,
            metrics.CorrectFactCount);

        Assert.Equal(
            0,
            metrics.IncorrectFactCount);

        Assert.Equal(
            0,
            metrics.MissedFactCount);

        /*
         * This evaluator deliberately does not invent false-alert
         * measurements. That requires a separate deterministic evaluation.
         */
        Assert.Equal(
            0,
            metrics.AlertEvaluatedStatementCount);

        Assert.Equal(
            0,
            metrics.FalseAlertStatementCount);

        Assert.Equal(
            2,
            extractor.CallCount);

        Assert.Equal(
            "offline-test-v1",
            extractor.LastPromptVersion);

        Assert.False(
            result.MayEnableRuntimeShadowMode);

        Assert.False(
            result.MayInfluencePersistence);

        /*
         * The result contract must remain aggregate-only.
         */
        var propertyNames =
            typeof(
                    BillStatementAiPrivateCorpusProviderEvaluationResult)
                .GetProperties()
                .Select(
                    property =>
                        property.Name)
                .ToArray();

        Assert.DoesNotContain(
            propertyNames,
            name =>
                name.Contains(
                    "Text",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Path",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "CaseId",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Evidence",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Candidate",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Evaluate_ProviderFailureIsCountedWithoutExposingFailureDetails()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            caseId:
                "provider-a-001",
            providerKey:
                "provider-a",
            totalAmount:
                104.99m);

        directory.WriteCase(
            caseId:
                "provider-b-001",
            providerKey:
                "provider-b",
            totalAmount:
                55m);

        var extractor =
            new FakeAiExtractor(
                failWhenDocumentContains:
                    "$55.00");

        var result =
            await CreateEvaluator(
                    extractor)
                .EvaluateAsync(
                    directory.Path,
                    [
                        "provider-a-001",
                        "provider-b-001"
                    ],
                    promptVersion:
                        "offline-test-v1",
                    providerCallsAuthorized:
                        true,
                    readinessPolicy:
                        CreateReadinessPolicy());

        Assert.True(
            result.ProviderEvaluationStarted);

        var metrics =
            Assert.IsType<BillStatementAiShadowReadinessMetrics>(
                result.Metrics);

        Assert.Equal(
            2,
            metrics.ProviderAttemptCount);

        Assert.Equal(
            1,
            metrics.ProviderFailureCount);

        Assert.Equal(
            1,
            metrics.ReadyCandidateStatementCount);

        Assert.Equal(
            4,
            metrics.CorrectFactCount);

        Assert.Equal(
            0,
            metrics.IncorrectFactCount);

        Assert.Equal(
            4,
            metrics.MissedFactCount);

        Assert.Equal(
            2,
            extractor.CallCount);

        Assert.DoesNotContain(
            "sensitive-provider-detail",
            result.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Evaluate_RejectedCandidateDoesNotBecomeTrustedExtraction()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            caseId:
                "provider-a-001",
            providerKey:
                "provider-a",
            totalAmount:
                104.99m);

        directory.WriteCase(
            caseId:
                "provider-b-001",
            providerKey:
                "provider-b",
            totalAmount:
                55m);

        var extractor =
            new FakeAiExtractor(
                returnUnsupportedEvidence:
                    true);

        var result =
            await CreateEvaluator(
                    extractor)
                .EvaluateAsync(
                    directory.Path,
                    [
                        "provider-a-001",
                        "provider-b-001"
                    ],
                    promptVersion:
                        "offline-test-v1",
                    providerCallsAuthorized:
                        true,
                    readinessPolicy:
                        CreateReadinessPolicy());

        var metrics =
            Assert.IsType<BillStatementAiShadowReadinessMetrics>(
                result.Metrics);

        /*
         * The provider successfully returned two candidates, so these are
         * attempts rather than transport failures.
         */
        Assert.Equal(
            2,
            metrics.ProviderAttemptCount);

        Assert.Equal(
            0,
            metrics.ProviderFailureCount);

        /*
         * Evidence validation rejects both candidates. They therefore count
         * as missed truth, never as trusted extractions.
         */
        Assert.Equal(
            0,
            metrics.ReadyCandidateStatementCount);

        Assert.Equal(
            0,
            metrics.CorrectFactCount);

        Assert.Equal(
            0,
            metrics.IncorrectFactCount);

        Assert.Equal(
            8,
            metrics.MissedFactCount);
    }

    private static BillStatementAiPrivateCorpusProviderEvaluator
        CreateEvaluator(
            IBillStatementAiExtractor extractor)
    {
        return new BillStatementAiPrivateCorpusProviderEvaluator(
            new BillStatementAiPrivateCorpusLoader(),
            new BillStatementAiPrivateCorpusCoverageGate(),
            extractor,
            new BillStatementAiCandidateConversionService(
                new BillStatementAiCandidateValidator()),
            new BillStatementAiGroundTruthScorer());
    }

    private static BillStatementAiShadowReadinessPolicy
        CreateReadinessPolicy()
    {
        return new BillStatementAiShadowReadinessPolicy(
            MinimumEvaluatedStatementCount:
                2,
            MinimumDistinctProviderCount:
                2,
            MinimumStatementsPerProvider:
                1,
            MinimumProviderAttemptCount:
                2,
            MinimumAlertEvaluatedStatementCount:
                2,
            MinimumFactPrecision:
                0.99m,
            MinimumFactRecall:
                0.95m,
            MinimumReadyCandidateRate:
                0.85m,
            MaximumFalseAlertRate:
                0.01m,
            MaximumProviderFailureRate:
                0.50m);
    }

    private sealed class FakeAiExtractor
        : IBillStatementAiExtractor
    {
        private readonly string?
            _failWhenDocumentContains;

        private readonly bool
            _returnUnsupportedEvidence;

        public FakeAiExtractor(
            string? failWhenDocumentContains = null,
            bool returnUnsupportedEvidence = false)
        {
            _failWhenDocumentContains =
                failWhenDocumentContains;

            _returnUnsupportedEvidence =
                returnUnsupportedEvidence;
        }

        public int CallCount { get; private set; }

        public string? LastPromptVersion { get; private set; }

        public Task<BillStatementAiCandidate> ExtractAsync(
            BillStatementAiExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(
                request);

            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;

            LastPromptVersion =
                request.PromptVersion;

            if (!string.IsNullOrWhiteSpace(
                    _failWhenDocumentContains) &&
                request.DocumentText.Contains(
                    _failWhenDocumentContains,
                    StringComparison.Ordinal))
            {
                throw new BillStatementAiExtractionException(
                    "sensitive-provider-detail");
            }

            var amount =
                request.DocumentText.Contains(
                    "$104.99",
                    StringComparison.Ordinal)
                    ? 104.99m
                    : request.DocumentText.Contains(
                        "$55.00",
                        StringComparison.Ordinal)
                        ? 55m
                        : throw new InvalidOperationException(
                            "Unexpected test statement.");

            IReadOnlyList<BillStatementAiEvidence> evidence =
                _returnUnsupportedEvidence
                    ?
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.TotalDue,
                            "This text does not exist in the statement."),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.BillingPeriodStart,
                            "This text does not exist in the statement."),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.BillingPeriodEnd,
                            "This text does not exist in the statement."),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.CurrencyCode,
                            "This text does not exist in the statement.")
                    ]
                    :
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.TotalDue,
                            amount ==
                                104.99m
                                ? "Total Due: $104.99"
                                : "Total Due: $55.00"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.BillingPeriodStart,
                            "Billing Period Start: 08/01/2026"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.BillingPeriodEnd,
                            "Billing Period End: 08/31/2026"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.CurrencyCode,
                            "Currency: USD")
                    ];

            return Task.FromResult(
                new BillStatementAiCandidate(
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
                        amount,
                    CurrencyCode:
                        "USD",
                    PlanOrService:
                        null,
                    UsageSummary:
                        null,
                    LineItems:
                        [],
                    Evidence:
                        evidence,
                    ModelConfidence:
                        BillStatementAiModelConfidence.High));
        }
    }

    private sealed class TemporaryCorpusDirectory
        : IDisposable
    {
        public TemporaryCorpusDirectory()
        {
            Path =
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"billwatch-provider-corpus-test-{Guid.NewGuid():N}");

            Directory.CreateDirectory(
                Path);
        }

        public string Path { get; }

        public void WriteCase(
            string caseId,
            string providerKey,
            decimal totalAmount)
        {
            var caseDirectory =
                System.IO.Path.Combine(
                    Path,
                    caseId);

            Directory.CreateDirectory(
                caseDirectory);

            var amountText =
                totalAmount.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture);

            File.WriteAllText(
                System.IO.Path.Combine(
                    caseDirectory,
                    BillStatementAiPrivateCorpusPathPolicy
                        .StatementTextFileName),
                $$"""
                Total Due: ${{amountText}}
                Billing Period Start: 08/01/2026
                Billing Period End: 08/31/2026
                Currency: USD
                """);

            File.WriteAllText(
                System.IO.Path.Combine(
                    caseDirectory,
                    BillStatementAiPrivateCorpusPathPolicy
                        .GroundTruthFileName),
                $$"""
                {
                  "providerKey": "{{providerKey}}",
                  "totalAmount": {{amountText}},
                  "billingPeriodStart": "2026-08-01",
                  "billingPeriodEnd": "2026-08-31",
                  "statementDate": null,
                  "dueDate": null,
                  "currencyCode": "USD",
                  "lineItems": []
                }
                """);
        }

        public void Dispose()
        {
            if (Directory.Exists(
                    Path))
            {
                Directory.Delete(
                    Path,
                    recursive:
                        true);
            }
        }
    }
}