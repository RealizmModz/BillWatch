namespace BillWatch.API.Services.Statements;

/*
 * Converts AI candidate output into BillWatch's existing structured
 * statement model only after deterministic evidence validation succeeds.
 *
 * This service is the trust boundary between a model response and the
 * rest of the statement-processing pipeline. A model can propose facts;
 * this service decides whether those proposals are eligible to move
 * deeper into BillWatch.
 */
public sealed class BillStatementAiCandidateConversionService
{
    private readonly BillStatementAiCandidateValidator
        _candidateValidator;

    public BillStatementAiCandidateConversionService(
        BillStatementAiCandidateValidator candidateValidator)
    {
        _candidateValidator =
            candidateValidator;
    }

    public BillStatementAiCandidateConversionResult Convert(
        string documentText,
        BillStatementAiCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(
            documentText);

        ArgumentNullException.ThrowIfNull(
            candidate);

        var validation =
            _candidateValidator.Validate(
                documentText,
                candidate);

        if (!validation.IsValid)
        {
            return BillStatementAiCandidateConversionResult.Rejected(
                validation.Errors);
        }

        var missingRequiredFields =
            GetMissingRequiredFields(
                candidate);

        var structuredStatement =
            new BillStatementStructuredData(
                TotalAmount:
                    RoundMoney(
                        candidate.TotalDue),

                BillingPeriodStart:
                    candidate.BillingPeriodStart,

                BillingPeriodEnd:
                    candidate.BillingPeriodEnd,

                StatementDate:
                    candidate.StatementDate,

                DueDate:
                    candidate.DueDate,

                CurrencyCode:
                    NormalizeCurrencyCode(
                        candidate.CurrencyCode),

                Confidence:
                    DetermineStructuredConfidence(
                        candidate,
                        missingRequiredFields.Count),

                MissingRequiredFields:
                    missingRequiredFields);

        var lineItems =
            ConvertLineItems(
                candidate.LineItems);

        var evidence =
            candidate.Evidence
                .Select(
                    item =>
                        new BillStatementExtractionEvidence(
                            FactKey:
                                item.FactKey.Trim(),

                            SourceText:
                                item.SourceExcerpt.Trim(),

                            PageNumber:
                                item.PageNumber))
                .ToList()
                .AsReadOnly();

        return BillStatementAiCandidateConversionResult.Accepted(
            new BillStatementExtractionResult(
                Statement:
                    structuredStatement,

                LineItems:
                    lineItems,

                Source:
                    BillStatementExtractionSource.AiAssisted,

                ExtractorVersion:
                    "ai-candidate-v1",

                Evidence:
                    evidence));
    }

    private static IReadOnlyList<string> GetMissingRequiredFields(
        BillStatementAiCandidate candidate)
    {
        var missing =
            new List<string>(
                capacity:
                    4);

        if (!candidate.TotalDue.HasValue)
        {
            missing.Add(
                nameof(
                    BillStatementStructuredData.TotalAmount));
        }

        if (!candidate.BillingPeriodStart.HasValue)
        {
            missing.Add(
                nameof(
                    BillStatementStructuredData.BillingPeriodStart));
        }

        if (!candidate.BillingPeriodEnd.HasValue)
        {
            missing.Add(
                nameof(
                    BillStatementStructuredData.BillingPeriodEnd));
        }

        if (string.IsNullOrWhiteSpace(
                candidate.CurrencyCode))
        {
            missing.Add(
                nameof(
                    BillStatementStructuredData.CurrencyCode));
        }

        return missing.AsReadOnly();
    }

    private static BillStatementStructuredDataConfidence
        DetermineStructuredConfidence(
            BillStatementAiCandidate candidate,
            int missingRequiredFieldCount)
    {
        /*
         * ModelConfidence is intentionally NOT consulted here.
         *
         * BillWatch confidence comes from deterministic completeness and
         * source-evidence validation, never from how confident a model says
         * it is.
         */
        if (missingRequiredFieldCount ==
            0)
        {
            return BillStatementStructuredDataConfidence.StrongEvidence;
        }

        var extractedFactCount =
            CountExtractedFacts(
                candidate);

        return extractedFactCount >
            0
                ? BillStatementStructuredDataConfidence.Partial
                : BillStatementStructuredDataConfidence.InsufficientEvidence;
    }

    private static int CountExtractedFacts(
        BillStatementAiCandidate candidate)
    {
        var count =
            0;

        if (!string.IsNullOrWhiteSpace(
                candidate.ProviderName))
        {
            count++;
        }

        if (!string.IsNullOrWhiteSpace(
                candidate.AccountIdentifierSuffix))
        {
            count++;
        }

        if (candidate.BillingPeriodStart.HasValue)
        {
            count++;
        }

        if (candidate.BillingPeriodEnd.HasValue)
        {
            count++;
        }

        if (candidate.StatementDate.HasValue)
        {
            count++;
        }

        if (candidate.DueDate.HasValue)
        {
            count++;
        }

        if (candidate.PreviousBalance.HasValue)
        {
            count++;
        }

        if (candidate.Payments.HasValue)
        {
            count++;
        }

        if (candidate.CurrentCharges.HasValue)
        {
            count++;
        }

        if (candidate.TotalDue.HasValue)
        {
            count++;
        }

        if (!string.IsNullOrWhiteSpace(
                candidate.CurrencyCode))
        {
            count++;
        }

        if (!string.IsNullOrWhiteSpace(
                candidate.PlanOrService))
        {
            count++;
        }

        if (!string.IsNullOrWhiteSpace(
                candidate.UsageSummary))
        {
            count++;
        }

        count +=
            candidate.LineItems.Count;

        return count;
    }

    private static IReadOnlyList<BillStatementStructuredLineItem>
        ConvertLineItems(
            IReadOnlyList<BillStatementAiLineItemCandidate> lineItems)
    {
        if (lineItems.Count ==
            0)
        {
            return [];
        }

        return lineItems
            .Select(
                item =>
                    new BillStatementStructuredLineItem(
                        Description:
                            item.Description.Trim(),

                        Amount:
                            decimal.Round(
                                item.Amount,
                                2,
                                MidpointRounding.AwayFromZero),

                        Category:
                            MapCategory(
                                item.Kind)))
            .ToList()
            .AsReadOnly();
    }

    private static string? MapCategory(
        BillStatementAiLineItemKind kind)
    {
        return kind switch
        {
            BillStatementAiLineItemKind.Service =>
                "Service",

            BillStatementAiLineItemKind.Fee =>
                "Fee",

            BillStatementAiLineItemKind.Tax =>
                "Tax",

            /*
             * Existing downstream change intelligence treats provider
             * discounts, credits and promotions as the same supported
             * reduction category. Preserve that domain behavior here.
             */
            BillStatementAiLineItemKind.Discount or
            BillStatementAiLineItemKind.Credit or
            BillStatementAiLineItemKind.Promotion =>
                "Discount",

            BillStatementAiLineItemKind.Equipment =>
                "Equipment",

            BillStatementAiLineItemKind.Usage =>
                "Usage",

            _ =>
                null
        };
    }

    private static decimal? RoundMoney(
        decimal? amount)
    {
        if (!amount.HasValue)
        {
            return null;
        }

        return decimal.Round(
            amount.Value,
            2,
            MidpointRounding.AwayFromZero);
    }

    private static string NormalizeCurrencyCode(
        string? currencyCode)
    {
        return string.IsNullOrWhiteSpace(
                currencyCode)
            ? string.Empty
            : currencyCode
                .Trim()
                .ToUpperInvariant();
    }
}

public sealed record BillStatementAiCandidateConversionResult(
    bool IsAccepted,
    BillStatementExtractionResult? Extraction,
    IReadOnlyList<string> Errors)
{
    public static BillStatementAiCandidateConversionResult Accepted(
        BillStatementExtractionResult extraction)
    {
        ArgumentNullException.ThrowIfNull(
            extraction);

        return new BillStatementAiCandidateConversionResult(
            IsAccepted:
                true,

            Extraction:
                extraction,

            Errors:
                []);
    }

    public static BillStatementAiCandidateConversionResult Rejected(
        IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(
            errors);

        return new BillStatementAiCandidateConversionResult(
            IsAccepted:
                false,

            Extraction:
                null,

            Errors:
                errors);
    }
}
