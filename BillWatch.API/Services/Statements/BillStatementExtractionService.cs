namespace BillWatch.API.Services.Statements
{
    public interface IBillStatementExtractionService
    {
        Task<BillStatementExtractionResult> ExtractAsync(
            BillStatementExtractionRequest request,
            CancellationToken cancellationToken = default);
    }

    public sealed record BillStatementExtractionRequest(
        string DocumentText,
        BillStatementExtractionHints Hints);

    /*
     * Hints are trusted server-side context that may help an extractor
     * interpret a document.
     *
     * They are not evidence and must never be used by themselves to prove
     * a statement fact.
     */
    public sealed record BillStatementExtractionHints(
        string? ExpectedProviderName,
        string? ExpectedCategory);

    public sealed record BillStatementExtractionResult(
        BillStatementStructuredData Statement,
        IReadOnlyList<BillStatementStructuredLineItem> LineItems,
        BillStatementExtractionSource Source,
        string ExtractorVersion,
        IReadOnlyList<BillStatementExtractionEvidence> Evidence)
    {
        public bool IsReadyForValidation =>
            Statement.IsReadyForPersistence;
    }

    /*
     * Evidence is deliberately modeled at the extraction boundary now so
     * future AI/provider adapters can return source-backed facts without
     * changing controllers, persistence, or the MAUI client.
     *
     * The existing deterministic parsers do not yet retain exact source
     * spans, so their implementation returns an empty collection rather
     * than manufacturing evidence.
     */
    public sealed record BillStatementExtractionEvidence(
        string FactKey,
        string SourceText,
        int? PageNumber = null);

    public enum BillStatementExtractionSource
    {
        Deterministic = 0,
        AiAssisted = 1,
        ProviderAdapter = 2,
        Hybrid = 3
    }

    public sealed class DeterministicBillStatementExtractionService
        : IBillStatementExtractionService
    {
        public const string Version =
            "deterministic-v1";

        private readonly DeterministicBillStatementParser
            _statementParser;

        private readonly DeterministicBillLineItemParser
            _lineItemParser;

        public DeterministicBillStatementExtractionService(
            DeterministicBillStatementParser statementParser,
            DeterministicBillLineItemParser lineItemParser)
        {
            _statementParser =
                statementParser;

            _lineItemParser =
                lineItemParser;
        }

        public Task<BillStatementExtractionResult> ExtractAsync(
            BillStatementExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(
                request);

            ArgumentNullException.ThrowIfNull(
                request.DocumentText);

            cancellationToken.ThrowIfCancellationRequested();

            var statement =
                _statementParser.Parse(
                    request.DocumentText);

            var lineItems =
                _lineItemParser.Parse(
                    request.DocumentText);

            return Task.FromResult(
                new BillStatementExtractionResult(
                    Statement:
                        statement,

                    LineItems:
                        lineItems,

                    Source:
                        BillStatementExtractionSource.Deterministic,

                    ExtractorVersion:
                        Version,

                    Evidence:
                        []));
        }
    }
}