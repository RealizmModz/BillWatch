namespace BillWatch.API.Services.Statements
{
    /*
     * Vendor-neutral AI boundary.
     *
     * OpenAI can implement this first, but the rest of BillWatch must
     * never depend directly on an OpenAI SDK or model type.
     */
    public interface IBillStatementAiExtractor
    {
        Task<BillStatementAiCandidate> ExtractAsync(
            BillStatementAiExtractionRequest request,
            CancellationToken cancellationToken = default);
    }

    public sealed record BillStatementAiExtractionRequest(
        string DocumentText,
        BillStatementExtractionHints Hints,
        string PromptVersion);

    /*
     * This is candidate information only.
     *
     * Nothing in this record is automatically trusted or persisted.
     * It must first pass BillWatch's deterministic evidence and
     * financial validation pipeline.
     */
    public sealed record BillStatementAiCandidate(
        string? ProviderName,
        string? AccountIdentifierSuffix,
        DateOnly? BillingPeriodStart,
        DateOnly? BillingPeriodEnd,
        DateOnly? StatementDate,
        DateOnly? DueDate,
        decimal? PreviousBalance,
        decimal? Payments,
        decimal? CurrentCharges,
        decimal? TotalDue,
        string? CurrencyCode,
        string? PlanOrService,
        string? UsageSummary,
        IReadOnlyList<BillStatementAiLineItemCandidate> LineItems,
        IReadOnlyList<BillStatementAiEvidence> Evidence,
        BillStatementAiModelConfidence ModelConfidence);

    public sealed record BillStatementAiLineItemCandidate(
        string Description,
        decimal Amount,
        BillStatementAiLineItemKind Kind);

    /*
     * SourceExcerpt must come from the extracted/OCR document text.
     *
     * A model explanation or its own reasoning is never acceptable as
     * evidence.
     */
    public sealed record BillStatementAiEvidence(
        string FactKey,
        string SourceExcerpt,
        int? PageNumber = null);

    /*
     * Model confidence is advisory only.
     *
     * BillWatch's final confidence is determined by deterministic
     * validation and comparison, not by this value.
     */
    public enum BillStatementAiModelConfidence
    {
        Unknown = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    public enum BillStatementAiLineItemKind
    {
        Unknown = 0,
        Service = 1,
        Fee = 2,
        Tax = 3,
        Discount = 4,
        Credit = 5,
        Promotion = 6,
        Equipment = 7,
        Usage = 8,
        Other = 9
    }

    /*
     * Stable fact keys will later be included in the structured-output
     * schema/prompt.
     *
     * Keeping them centralized prevents model/provider implementations
     * from inventing incompatible naming conventions.
     */
    public static class BillStatementAiFactKeys
    {
        public const string ProviderName =
            "providerName";

        public const string AccountIdentifierSuffix =
            "accountIdentifierSuffix";

        public const string BillingPeriodStart =
            "billingPeriodStart";

        public const string BillingPeriodEnd =
            "billingPeriodEnd";

        public const string StatementDate =
            "statementDate";

        public const string DueDate =
            "dueDate";

        public const string PreviousBalance =
            "previousBalance";

        public const string Payments =
            "payments";

        public const string CurrentCharges =
            "currentCharges";

        public const string TotalDue =
            "totalDue";

        public const string CurrencyCode =
            "currencyCode";

        public const string PlanOrService =
            "planOrService";

        public const string UsageSummary =
            "usageSummary";

        public static string LineItemDescription(
            int index)
        {
            return
                $"lineItems[{index}].description";
        }

        public static string LineItemAmount(
            int index)
        {
            return
                $"lineItems[{index}].amount";
        }
    }

    /*
     * Vendor-neutral provider failure.
     *
     * Orchestration may safely fall back on this exception without
     * depending on an OpenAI-specific implementation type.
     */
    public sealed class BillStatementAiExtractionException
        : Exception
    {
        public BillStatementAiExtractionException(
            string message)
            : base(message)
        {
        }

        public BillStatementAiExtractionException(
            string message,
            Exception innerException)
            : base(
                message,
                innerException)
        {
        }
    }
}
