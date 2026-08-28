using System.Globalization;
using System.Text.RegularExpressions;

namespace BillWatch.API.Services.Statements;

public sealed partial class DeterministicBillStatementParser
{
    private static readonly CultureInfo UsCulture =
        CultureInfo.GetCultureInfo(
            "en-US");

    private static readonly string[] AcceptedDateFormats =
    [
        "M/d/yyyy",
        "MM/dd/yyyy",
        "M/d/yy",
        "MM/dd/yy",

        "M-d-yyyy",
        "MM-dd-yyyy",
        "M-d-yy",
        "MM-dd-yy",

        "MMM d yyyy",
        "MMM dd yyyy",
        "MMM d, yyyy",
        "MMM dd, yyyy",

        "MMMM d yyyy",
        "MMMM dd yyyy",
        "MMMM d, yyyy",
        "MMMM dd, yyyy"
    ];

    public BillStatementStructuredData Parse(
        string extractedText)
    {
        ArgumentNullException.ThrowIfNull(
            extractedText);

        if (string.IsNullOrWhiteSpace(
                extractedText))
        {
            return CreateEmptyResult();
        }

        var totalAmount =
            TryExtractTotalAmount(
                extractedText);

        var billingPeriod =
            TryExtractBillingPeriod(
                extractedText);

        var statementDate =
            TryExtractDate(
                StatementDateRegex(),
                extractedText);

        var dueDate =
            TryExtractDate(
                DueDateRegex(),
                extractedText);

        var missingRequiredFields =
            new List<string>(
                capacity:
                    3);

        if (!totalAmount.HasValue)
        {
            missingRequiredFields.Add(
                nameof(
                    BillStatementStructuredData.TotalAmount));
        }

        if (!billingPeriod.Start.HasValue)
        {
            missingRequiredFields.Add(
                nameof(
                    BillStatementStructuredData.BillingPeriodStart));
        }

        if (!billingPeriod.End.HasValue)
        {
            missingRequiredFields.Add(
                nameof(
                    BillStatementStructuredData.BillingPeriodEnd));
        }

        var confidence =
            DetermineConfidence(
                missingRequiredFields.Count,
                statementDate,
                dueDate);

        return new BillStatementStructuredData(
            TotalAmount:
                totalAmount,

            BillingPeriodStart:
                billingPeriod.Start,

            BillingPeriodEnd:
                billingPeriod.End,

            StatementDate:
                statementDate,

            DueDate:
                dueDate,

            CurrencyCode:
                "USD",

            Confidence:
                confidence,

            MissingRequiredFields:
                missingRequiredFields.AsReadOnly());
    }

    private static decimal? TryExtractTotalAmount(
        string text)
    {
        Match? bestMatch =
            null;

        var bestRank =
            int.MaxValue;

        foreach (Match match in
                 TotalAmountRegex()
                     .Matches(text))
        {
            if (!match.Success)
            {
                continue;
            }

            var label =
                match.Groups["label"]
                    .Value;

            var rank =
                RankAmountLabel(
                    label);

            if (rank >=
                bestRank)
            {
                continue;
            }

            bestMatch =
                match;

            bestRank =
                rank;
        }

        if (bestMatch is null)
        {
            return null;
        }

        return TryParseMoney(
            bestMatch
                .Groups["amount"]
                .Value);
    }

    private static (
        DateOnly? Start,
        DateOnly? End)
        TryExtractBillingPeriod(
            string text)
    {
        var match =
            BillingPeriodRegex()
                .Match(text);

        if (!match.Success)
        {
            return (
                null,
                null);
        }

        var start =
            TryParseDate(
                match.Groups["start"]
                    .Value);

        var end =
            TryParseDate(
                match.Groups["end"]
                    .Value);

        if (!start.HasValue ||
            !end.HasValue ||
            end.Value <
                start.Value)
        {
            return (
                null,
                null);
        }

        return (
            start,
            end);
    }

    private static DateOnly? TryExtractDate(
        Regex regex,
        string text)
    {
        var match =
            regex.Match(
                text);

        if (!match.Success)
        {
            return null;
        }

        return TryParseDate(
            match.Groups["date"]
                .Value);
    }

    private static decimal? TryParseMoney(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        var normalized =
            value
                .Trim()
                .Replace(
                    "$",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    ",",
                    string.Empty,
                    StringComparison.Ordinal)
                .Trim();

        var isParenthesized =
            normalized.Length >=
                2 &&
            normalized[0] ==
                '(' &&
            normalized[^1] ==
                ')';

        if (isParenthesized)
        {
            normalized =
                normalized[1..^1]
                    .Trim();
        }

        if (!decimal.TryParse(
                normalized,
                NumberStyles.AllowDecimalPoint |
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var amount))
        {
            return null;
        }

        if (isParenthesized)
        {
            amount =
                -amount;
        }

        if (amount <
            0m)
        {
            return null;
        }

        return decimal.Round(
            amount,
            2,
            MidpointRounding.AwayFromZero);
    }

    private static DateOnly? TryParseDate(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        var normalized =
            Regex.Replace(
                value.Trim(),
                @"\s+",
                " ");

        if (!DateOnly.TryParseExact(
                normalized,
                AcceptedDateFormats,
                UsCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
        {
            return null;
        }

        return parsed;
    }

    private static int RankAmountLabel(
        string label)
    {
        var normalized =
            Regex.Replace(
                    label.Trim(),
                    @"\s+",
                    " ")
                .ToLowerInvariant();

        return normalized switch
        {
            "total amount due" =>
                0,

            "current amount due" =>
                1,

            "total due" =>
                2,

            "amount due" =>
                3,

            "balance due" =>
                4,

            _ =>
                int.MaxValue
        };
    }

    private static BillStatementStructuredDataConfidence
        DetermineConfidence(
            int missingRequiredFieldCount,
            DateOnly? statementDate,
            DateOnly? dueDate)
    {
        if (missingRequiredFieldCount ==
            0)
        {
            return
                BillStatementStructuredDataConfidence
                    .StrongEvidence;
        }

        if (missingRequiredFieldCount <=
                2 &&
            (statementDate.HasValue ||
             dueDate.HasValue))
        {
            return
                BillStatementStructuredDataConfidence
                    .Partial;
        }

        return
            BillStatementStructuredDataConfidence
                .InsufficientEvidence;
    }

    private static BillStatementStructuredData
        CreateEmptyResult()
    {
        return new BillStatementStructuredData(
            TotalAmount:
                null,

            BillingPeriodStart:
                null,

            BillingPeriodEnd:
                null,

            StatementDate:
                null,

            DueDate:
                null,

            CurrencyCode:
                "USD",

            Confidence:
                BillStatementStructuredDataConfidence
                    .InsufficientEvidence,

            MissingRequiredFields:
                Array.AsReadOnly(
                    new[]
                    {
                        nameof(
                            BillStatementStructuredData.TotalAmount),

                        nameof(
                            BillStatementStructuredData.BillingPeriodStart),

                        nameof(
                            BillStatementStructuredData.BillingPeriodEnd)
                    }));
    }

    [GeneratedRegex(
        @"(?im)^\s*(?<label>total\s+amount\s+due|current\s+amount\s+due|total\s+due|amount\s+due|balance\s+due)\s*:?\s*(?<amount>\$?\s*\(?-?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d{1,2})?\)?)\s*(?:USD)?\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex
        TotalAmountRegex();

    [GeneratedRegex(
        @"(?im)^\s*(?:billing\s+period|service\s+period|billing\s+cycle|service\s+dates)\s*:?\s*(?<start>[A-Za-z]{3,9}\s+\d{1,2},?\s+\d{4}|\d{1,2}[/-]\d{1,2}[/-]\d{2,4})\s*(?:-|–|—|to|through)\s*(?<end>[A-Za-z]{3,9}\s+\d{1,2},?\s+\d{4}|\d{1,2}[/-]\d{1,2}[/-]\d{2,4})\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex
        BillingPeriodRegex();

    [GeneratedRegex(
        @"(?im)^\s*(?:statement\s+date|bill\s+date|invoice\s+date)\s*:?\s*(?<date>[A-Za-z]{3,9}\s+\d{1,2},?\s+\d{4}|\d{1,2}[/-]\d{1,2}[/-]\d{2,4})\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex
        StatementDateRegex();

    [GeneratedRegex(
        @"(?im)^\s*(?:due\s+date|payment\s+due|payment\s+due\s+date)\s*:?\s*(?<date>[A-Za-z]{3,9}\s+\d{1,2},?\s+\d{4}|\d{1,2}[/-]\d{1,2}[/-]\d{2,4})\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex
        DueDateRegex();
}

public sealed record BillStatementStructuredData(
    decimal? TotalAmount,
    DateOnly? BillingPeriodStart,
    DateOnly? BillingPeriodEnd,
    DateOnly? StatementDate,
    DateOnly? DueDate,
    string CurrencyCode,
    BillStatementStructuredDataConfidence Confidence,
    IReadOnlyList<string> MissingRequiredFields)
{
    public bool IsReadyForPersistence =>
        TotalAmount.HasValue &&
        BillingPeriodStart.HasValue &&
        BillingPeriodEnd.HasValue;
}

public enum BillStatementStructuredDataConfidence
{
    InsufficientEvidence = 0,
    Partial = 1,
    StrongEvidence = 2
}