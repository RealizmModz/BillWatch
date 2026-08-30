using System.Globalization;
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

        private static readonly Regex MoneyValueRegex =
            new(
                @"(?<open>\()?\s*(?<signBefore>[+-])?\s*(?:(?:USD|CAD|EUR|GBP)\s*)?[$€£]?\s*(?<signAfter>[+-])?\s*(?<number>\d[\d,]*(?:\.\d+)?)\s*(?<close>\))?",
                RegexOptions.Compiled |
                RegexOptions.CultureInvariant |
                RegexOptions.IgnoreCase);

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

                if (IsOutsideMoneyRange(
                        item.Amount))
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

            var evidenceByFact =
                new Dictionary<string, List<string>>(
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

                if (!evidenceByFact.TryGetValue(
                        factKey,
                        out var excerpts))
                {
                    excerpts =
                        [];

                    evidenceByFact.Add(
                        factKey,
                        excerpts);
                }

                excerpts.Add(
                    normalizedExcerpt);
            }

            RequireStringEvidence(
                candidate.ProviderName,
                BillStatementAiFactKeys.ProviderName,
                evidenceByFact,
                errors);

            RequireStringEvidence(
                candidate.AccountIdentifierSuffix,
                BillStatementAiFactKeys.AccountIdentifierSuffix,
                evidenceByFact,
                errors);

            RequireDateEvidence(
                candidate.BillingPeriodStart,
                BillStatementAiFactKeys.BillingPeriodStart,
                evidenceByFact,
                errors);

            RequireDateEvidence(
                candidate.BillingPeriodEnd,
                BillStatementAiFactKeys.BillingPeriodEnd,
                evidenceByFact,
                errors);

            RequireDateEvidence(
                candidate.StatementDate,
                BillStatementAiFactKeys.StatementDate,
                evidenceByFact,
                errors);

            RequireDateEvidence(
                candidate.DueDate,
                BillStatementAiFactKeys.DueDate,
                evidenceByFact,
                errors);

            RequireMoneyEvidence(
                candidate.PreviousBalance,
                BillStatementAiFactKeys.PreviousBalance,
                evidenceByFact,
                errors);

            RequireMoneyEvidence(
                candidate.Payments,
                BillStatementAiFactKeys.Payments,
                evidenceByFact,
                errors);

            RequireMoneyEvidence(
                candidate.CurrentCharges,
                BillStatementAiFactKeys.CurrentCharges,
                evidenceByFact,
                errors);

            RequireMoneyEvidence(
                candidate.TotalDue,
                BillStatementAiFactKeys.TotalDue,
                evidenceByFact,
                errors);

            RequireStringEvidence(
                candidate.CurrencyCode,
                BillStatementAiFactKeys.CurrencyCode,
                evidenceByFact,
                errors);

            RequireStringEvidence(
                candidate.PlanOrService,
                BillStatementAiFactKeys.PlanOrService,
                evidenceByFact,
                errors);

            RequireStringEvidence(
                candidate.UsageSummary,
                BillStatementAiFactKeys.UsageSummary,
                evidenceByFact,
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
                var lineItem =
                    candidate.LineItems[index];

                if (lineItem is null)
                {
                    continue;
                }

                RequireStringEvidence(
                    lineItem.Description,
                    BillStatementAiFactKeys
                        .LineItemDescription(
                            index),
                    evidenceByFact,
                    errors);

                RequireMoneyEvidence(
                    lineItem.Amount,
                    BillStatementAiFactKeys
                        .LineItemAmount(
                            index),
                    evidenceByFact,
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

            if (IsOutsideMoneyRange(
                    value.Value))
            {
                errors.Add(
                    $"'{fieldName}' contains an invalid monetary value.");
            }
        }

        private static void RequireStringEvidence(
            string? value,
            string factKey,
            IReadOnlyDictionary<string, List<string>> evidenceByFact,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return;
            }

            RequireEvidenceValue(
                factKey,
                evidenceByFact,
                excerpt =>
                    excerpt.Contains(
                        NormalizeEvidenceText(
                            value),
                        StringComparison.OrdinalIgnoreCase),
                errors);
        }

        private static bool IsOutsideMoneyRange(
            decimal value)
        {
            /*
             * Direct bounds avoid Math.Abs(decimal.MinValue), which throws
             * before the candidate can be rejected safely.
             */
            return value >
                    MaxAbsoluteMoneyValue ||
                value <
                    -MaxAbsoluteMoneyValue;
        }

        private static void RequireDateEvidence(
            DateOnly? value,
            string factKey,
            IReadOnlyDictionary<string, List<string>> evidenceByFact,
            ICollection<string> errors)
        {
            if (!value.HasValue)
            {
                return;
            }

            var supportedRepresentations =
                new[]
                {
                    value.Value.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture),

                    value.Value.ToString(
                        "M/d/yyyy",
                        CultureInfo.InvariantCulture),

                    value.Value.ToString(
                        "MM/dd/yyyy",
                        CultureInfo.InvariantCulture),

                    value.Value.ToString(
                        "M-d-yyyy",
                        CultureInfo.InvariantCulture),

                    value.Value.ToString(
                        "MM-dd-yyyy",
                        CultureInfo.InvariantCulture),

                    value.Value.ToString(
                        "MMMM d, yyyy",
                        CultureInfo.InvariantCulture),

                    value.Value.ToString(
                        "MMM d, yyyy",
                        CultureInfo.InvariantCulture),

                    value.Value.ToString(
                        "d MMMM yyyy",
                        CultureInfo.InvariantCulture),

                    value.Value.ToString(
                        "d MMM yyyy",
                        CultureInfo.InvariantCulture)
                };

            RequireEvidenceValue(
                factKey,
                evidenceByFact,
                excerpt =>
                    supportedRepresentations.Any(
                        representation =>
                            excerpt.Contains(
                                representation,
                                StringComparison.OrdinalIgnoreCase)),
                errors);
        }

        private static void RequireMoneyEvidence(
            decimal? value,
            string factKey,
            IReadOnlyDictionary<string, List<string>> evidenceByFact,
            ICollection<string> errors)
        {
            if (!value.HasValue)
            {
                return;
            }

            RequireEvidenceValue(
                factKey,
                evidenceByFact,
                excerpt =>
                    ExtractMoneyValues(
                            excerpt)
                        .Contains(
                            value.Value),
                errors);
        }

        private static void RequireMoneyEvidence(
            decimal value,
            string factKey,
            IReadOnlyDictionary<string, List<string>> evidenceByFact,
            ICollection<string> errors)
        {
            RequireMoneyEvidence(
                (decimal?)value,
                factKey,
                evidenceByFact,
                errors);
        }

        private static void RequireEvidenceValue(
            string factKey,
            IReadOnlyDictionary<string, List<string>> evidenceByFact,
            Func<string, bool> supportsValue,
            ICollection<string> errors)
        {
            if (!evidenceByFact.TryGetValue(
                    factKey,
                    out var excerpts) ||
                excerpts.Count ==
                    0)
            {
                errors.Add(
                    $"The extracted fact '{factKey}' does not have verified source evidence.");

                return;
            }

            if (excerpts.Any(
                    supportsValue))
            {
                return;
            }

            errors.Add(
                $"The verified evidence for '{factKey}' does not contain the extracted value.");
        }

        private static IReadOnlyList<decimal> ExtractMoneyValues(
            string excerpt)
        {
            var values =
                new List<decimal>();

            foreach (Match match in
                     MoneyValueRegex.Matches(
                         excerpt))
            {
                var numericText =
                    match.Groups["number"]
                        .Value
                        .Replace(
                            ",",
                            string.Empty,
                            StringComparison.Ordinal);

                if (!decimal.TryParse(
                        numericText,
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out var parsedValue))
                {
                    continue;
                }

                var isNegative =
                    match.Groups["signBefore"].Value ==
                        "-" ||
                    match.Groups["signAfter"].Value ==
                        "-" ||
                    match.Groups["open"].Success &&
                    match.Groups["close"].Success;

                values.Add(
                    isNegative
                        ? -parsedValue
                        : parsedValue);
            }

            return values;
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
