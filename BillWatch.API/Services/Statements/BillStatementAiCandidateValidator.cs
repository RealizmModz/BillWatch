using System.Text.RegularExpressions;

namespace BillWatch.API.Services.Statements
{
    /*
     * First deterministic gate between an AI model and BillWatch's
     * trusted statement pipeline.
     *
     * Passing this validator does NOT mean the candidate is safe to
     * persist. Existing statement validation, Bill Stream matching,
     * reconciliation and historical comparison still happen later.
     */
    public sealed class BillStatementAiCandidateValidator
    {
        private const int MaxProviderNameLength =
            200;

        private const int MaxAccountSuffixLength =
            8;

        private const int MaxPlanOrServiceLength =
            300;

        private const int MaxUsageSummaryLength =
            500;

        private const int MaxLineItems =
            100;

        private const int MaxLineItemDescriptionLength =
            200;

        private const int MaxEvidenceItems =
            250;

        private const int MaxEvidenceExcerptLength =
            500;

        private const decimal MaxAbsoluteMoneyValue =
            1_000_000m;

        public BillStatementAiCandidateValidationResult Validate(
            string documentText,
            BillStatementAiCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(
                documentText);

            ArgumentNullException.ThrowIfNull(
                candidate);

            var errors =
                new List<string>();

            if (string.IsNullOrWhiteSpace(
                    documentText))
            {
                errors.Add(
                    "The source document text is empty.");

                return CreateResult(
                    errors);
            }

            ValidateStrings(
                candidate,
                errors);

            ValidateMoney(
                candidate,
                errors);

            ValidateDates(
                candidate,
                errors);

            ValidateLineItems(
                candidate,
                errors);

            ValidateEvidence(
                documentText,
                candidate,
                errors);

            return CreateResult(
                errors);
        }

        private static void ValidateStrings(
            BillStatementAiCandidate candidate,
            ICollection<string> errors)
        {
            ValidateOptionalLength(
                candidate.ProviderName,
                BillStatementAiFactKeys.ProviderName,
                MaxProviderNameLength,
                errors);

            ValidateOptionalLength(
                candidate.PlanOrService,
                BillStatementAiFactKeys.PlanOrService,
                MaxPlanOrServiceLength,
                errors);

            ValidateOptionalLength(
                candidate.UsageSummary,
                BillStatementAiFactKeys.UsageSummary,
                MaxUsageSummaryLength,
                errors);

            if (!string.IsNullOrWhiteSpace(
                    candidate.AccountIdentifierSuffix))
            {
                var suffix =
                    candidate.AccountIdentifierSuffix
                        .Trim();

                if (suffix.Length >
                    MaxAccountSuffixLength)
                {
                    errors.Add(
                        "The account identifier suffix is too long.");
                }

                if (suffix.Any(
                        character =>
                            !char.IsLetterOrDigit(
                                character)))
                {
                    errors.Add(
                        "The account identifier suffix may contain only letters and digits.");
                }
            }

            if (!string.IsNullOrWhiteSpace(
                    candidate.CurrencyCode))
            {
                var currencyCode =
                    candidate.CurrencyCode
                        .Trim();

                if (currencyCode.Length !=
                        3 ||
                    currencyCode.Any(
                        character =>
                            !char.IsLetter(
                                character)))
                {
                    errors.Add(
                        "The currency code must be a three-letter code.");
                }
            }
        }

        private static void ValidateMoney(
            BillStatementAiCandidate candidate,
            ICollection<string> errors)
        {
            ValidateMoneyValue(
                candidate.PreviousBalance,
                BillStatementAiFactKeys.PreviousBalance,
                errors);

            ValidateMoneyValue(
                candidate.Payments,
                BillStatementAiFactKeys.Payments,
                errors);

            ValidateMoneyValue(
                candidate.CurrentCharges,
                BillStatementAiFactKeys.CurrentCharges,
                errors);

            ValidateMoneyValue(
                candidate.TotalDue,
                BillStatementAiFactKeys.TotalDue,
                errors);
        }

        private static void ValidateDates(
            BillStatementAiCandidate candidate,
            ICollection<string> errors)
        {
            if (candidate.BillingPeriodStart.HasValue &&
                candidate.BillingPeriodEnd.HasValue &&
                candidate.BillingPeriodStart.Value >
                    candidate.BillingPeriodEnd.Value)
            {
                errors.Add(
                    "The billing period start is after the billing period end.");
            }
        }

        private static void ValidateLineItems(
            BillStatementAiCandidate candidate,
            ICollection<string> errors)
        {
            if (candidate.LineItems is
                null)
            {
                errors.Add(
                    "The line-item collection is missing.");

                return;
            }

            if (candidate.LineItems.Count >
                MaxLineItems)
            {
                errors.Add(
                    $"The candidate contains more than {MaxLineItems} line items.");

                return;
            }

            for (var index = 0;
                 index < candidate.LineItems.Count;
                 index++)
            {
                var item =
                    candidate.LineItems[index];

                if (item is
                    null)
                {
                    errors.Add(
                        $"Line item {index} is missing.");

                    continue;
                }

                var description =
                    item.Description?
                        .Trim();

                if (string.IsNullOrWhiteSpace(
                        description))
                {
                    errors.Add(
                        $"Line item {index} does not have a description.");
                }
                else if (description.Length >
                    MaxLineItemDescriptionLength)
                {
                    errors.Add(
                        $"Line item {index} has an excessively long description.");
                }

                if (Math.Abs(
                        item.Amount) >
                    MaxAbsoluteMoneyValue)
                {
                    errors.Add(
                        $"Line item {index} has an invalid monetary amount.");
                }
            }
        }

        private static void ValidateEvidence(
            string documentText,
            BillStatementAiCandidate candidate,
            ICollection<string> errors)
        {
            if (candidate.Evidence is
                null)
            {
                errors.Add(
                    "The evidence collection is missing.");

                return;
            }

            if (candidate.Evidence.Count >
                MaxEvidenceItems)
            {
                errors.Add(
                    $"The candidate contains more than {MaxEvidenceItems} evidence items.");

                return;
            }

            var normalizedDocument =
                NormalizeEvidenceText(
                    documentText);

            var evidenceKeys =
                new HashSet<string>(
                    StringComparer.Ordinal);

            for (var index = 0;
                 index < candidate.Evidence.Count;
                 index++)
            {
                var evidence =
                    candidate.Evidence[index];

                if (evidence is
                    null)
                {
                    errors.Add(
                        $"Evidence item {index} is missing.");

                    continue;
                }

                var factKey =
                    evidence.FactKey?
                        .Trim();

                var sourceExcerpt =
                    evidence.SourceExcerpt?
                        .Trim();

                if (string.IsNullOrWhiteSpace(
                        factKey))
                {
                    errors.Add(
                        $"Evidence item {index} does not identify a fact.");

                    continue;
                }

                if (string.IsNullOrWhiteSpace(
                        sourceExcerpt))
                {
                    errors.Add(
                        $"Evidence for '{factKey}' does not contain a source excerpt.");

                    continue;
                }

                if (sourceExcerpt.Length >
                    MaxEvidenceExcerptLength)
                {
                    errors.Add(
                        $"Evidence for '{factKey}' is too long.");

                    continue;
                }

                var normalizedExcerpt =
                    NormalizeEvidenceText(
                        sourceExcerpt);

                /*
                 * Critical hallucination guard:
                 *
                 * the model cannot cite text that isn't actually
                 * present in the document supplied to it.
                 */
                if (!normalizedDocument.Contains(
                        normalizedExcerpt,
                        StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"Evidence for '{factKey}' was not found in the source document.");

                    continue;
                }

                evidenceKeys.Add(
                    factKey);
            }

            RequireEvidence(
                candidate.ProviderName,
                BillStatementAiFactKeys.ProviderName,
                evidenceKeys,
                errors);

            RequireEvidence(
                candidate.AccountIdentifierSuffix,
                BillStatementAiFactKeys.AccountIdentifierSuffix,
                evidenceKeys,
                errors);

            RequireEvidence(
                candidate.BillingPeriodStart,
                BillStatementAiFactKeys.BillingPeriodStart,
                evidenceKeys,
                errors);

            RequireEvidence(
                candidate.BillingPeriodEnd,
                BillStatementAiFactKeys.BillingPeriodEnd,
                evidenceKeys,
                errors);

            RequireEvidence(
                candidate.StatementDate,
                BillStatementAiFactKeys.StatementDate,
                evidenceKeys,
                errors);

            RequireEvidence(
                candidate.DueDate,
                BillStatementAiFactKeys.DueDate,
                evidenceKeys,
                errors);

            RequireEvidence(
                candidate.PreviousBalance,
                BillStatementAiFactKeys.PreviousBalance,
                evidenceKeys,
                errors);

            RequireEvidence(
                candidate.Payments,
                BillStatementAiFactKeys.Payments,
                evidenceKeys,
                errors);

            RequireEvidence(
                candidate.CurrentCharges,
                BillStatementAiFactKeys.CurrentCharges,
                evidenceKeys,
                errors);

            RequireEvidence(
                candidate.TotalDue,
                BillStatementAiFactKeys.TotalDue,
                evidenceKeys,
                errors);

            RequireEvidence(
                candidate.CurrencyCode,
                BillStatementAiFactKeys.CurrencyCode,
                evidenceKeys,
                errors);

            RequireEvidence(
                candidate.PlanOrService,
                BillStatementAiFactKeys.PlanOrService,
                evidenceKeys,
                errors);

            RequireEvidence(
                candidate.UsageSummary,
                BillStatementAiFactKeys.UsageSummary,
                evidenceKeys,
                errors);

            if (candidate.LineItems is
                null)
            {
                return;
            }

            for (var index = 0;
                 index < candidate.LineItems.Count;
                 index++)
            {
                RequireEvidenceKey(
                    BillStatementAiFactKeys
                        .LineItemDescription(
                            index),
                    evidenceKeys,
                    errors);

                RequireEvidenceKey(
                    BillStatementAiFactKeys
                        .LineItemAmount(
                            index),
                    evidenceKeys,
                    errors);
            }
        }

        private static void ValidateOptionalLength(
            string? value,
            string fieldName,
            int maximumLength,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return;
            }

            if (value.Trim().Length >
                maximumLength)
            {
                errors.Add(
                    $"'{fieldName}' exceeds the maximum supported length.");
            }
        }

        private static void ValidateMoneyValue(
            decimal? value,
            string fieldName,
            ICollection<string> errors)
        {
            if (!value.HasValue)
            {
                return;
            }

            if (Math.Abs(
                    value.Value) >
                MaxAbsoluteMoneyValue)
            {
                errors.Add(
                    $"'{fieldName}' contains an invalid monetary value.");
            }
        }

        private static void RequireEvidence<T>(
            T? value,
            string factKey,
            IReadOnlySet<string> evidenceKeys,
            ICollection<string> errors)
        {
            if (value is
                null)
            {
                return;
            }

            if (value is
                    string text &&
                string.IsNullOrWhiteSpace(
                    text))
            {
                return;
            }

            RequireEvidenceKey(
                factKey,
                evidenceKeys,
                errors);
        }

        private static void RequireEvidenceKey(
            string factKey,
            IReadOnlySet<string> evidenceKeys,
            ICollection<string> errors)
        {
            if (evidenceKeys.Contains(
                    factKey))
            {
                return;
            }

            errors.Add(
                $"The extracted fact '{factKey}' does not have verified source evidence.");
        }

        private static string NormalizeEvidenceText(
            string value)
        {
            return Regex.Replace(
                    value,
                    @"\s+",
                    " ",
                    RegexOptions.CultureInvariant)
                .Trim();
        }

        private static BillStatementAiCandidateValidationResult
            CreateResult(
                IReadOnlyCollection<string> errors)
        {
            return new BillStatementAiCandidateValidationResult(
                IsValid:
                    errors.Count ==
                    0,

                Errors:
                    errors.ToList()
                        .AsReadOnly());
        }
    }

    public sealed record BillStatementAiCandidateValidationResult(
        bool IsValid,
        IReadOnlyList<string> Errors);
}