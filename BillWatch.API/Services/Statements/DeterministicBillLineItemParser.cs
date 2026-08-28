using System.Globalization;
using System.Text.RegularExpressions;

namespace BillWatch.API.Services.Statements;

public sealed partial class DeterministicBillLineItemParser
{
    private const int MaxLineItems =
        100;

    private const decimal MaxAbsoluteLineItemAmount =
        1_000_000m;

    private static readonly HashSet<string>
        ChargeSectionHeaders =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            "charges",
            "current charges",
            "monthly charges",
            "service charges",
            "services and charges",
            "details of charges",
            "charge details",
            "bill details",
            "your charges",
            "current monthly charges"
        };

    public IReadOnlyList<BillStatementStructuredLineItem>
        Parse(
            string extractedText)
    {
        ArgumentNullException.ThrowIfNull(
            extractedText);

        if (string.IsNullOrWhiteSpace(
                extractedText))
        {
            return [];
        }

        var lines =
            extractedText
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n')
                .Split(
                    '\n');

        var results =
            new List<BillStatementStructuredLineItem>();

        var insideChargeSection =
            false;

        foreach (var rawLine in
                 lines)
        {
            var line =
                rawLine.Trim();

            if (line.Length ==
                0)
            {
                continue;
            }

            var normalizedLine =
                NormalizeLabel(
                    line);

            if (ChargeSectionHeaders.Contains(
                    normalizedLine))
            {
                insideChargeSection =
                    true;

                continue;
            }

            if (!insideChargeSection)
            {
                continue;
            }

            if (IsSectionTerminator(
                    normalizedLine))
            {
                insideChargeSection =
                    false;

                continue;
            }

            if (!TryParseLineItem(
                    line,
                    out var item))
            {
                continue;
            }

            results.Add(
                item);

            if (results.Count >=
                MaxLineItems)
            {
                break;
            }
        }

        return results.AsReadOnly();
    }

    private static bool TryParseLineItem(
        string line,
        out BillStatementStructuredLineItem item)
    {
        item =
            default!;

        var match =
            LineItemRegex()
                .Match(
                    line);

        if (!match.Success)
        {
            return false;
        }

        var description =
            match.Groups["description"]
                .Value
                .Trim()
                .TrimEnd(
                    ':',
                    '-',
                    '–',
                    '—')
                .Trim();

        if (description.Length <
                2 ||
            description.Length >
                200 ||
            IsIgnoredDescription(
                description))
        {
            return false;
        }

        if (!TryParseSignedMoney(
                match.Groups["amount"]
                    .Value,
                out var amount))
        {
            return false;
        }

        if (amount ==
                0m ||
            Math.Abs(
                amount) >
            MaxAbsoluteLineItemAmount)
        {
            return false;
        }

        item =
            new BillStatementStructuredLineItem(
                Description:
                    description,

                Amount:
                    amount,

                Category:
                    ClassifyCategory(
                        description));

        return true;
    }

    private static bool TryParseSignedMoney(
        string value,
        out decimal amount)
    {
        amount =
            0m;

        if (string.IsNullOrWhiteSpace(
                value))
        {
            return false;
        }

        var normalized =
            value
                .Trim();

        var parenthesized =
            normalized.Length >=
                2 &&
            normalized[0] ==
                '(' &&
            normalized[^1] ==
                ')';

        if (parenthesized)
        {
            normalized =
                normalized[1..^1];
        }

        normalized =
            normalized
                .Replace(
                    "$",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    ",",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    " ",
                    string.Empty,
                    StringComparison.Ordinal);

        if (!decimal.TryParse(
                normalized,
                NumberStyles.AllowLeadingSign |
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out amount))
        {
            return false;
        }

        if (parenthesized)
        {
            amount =
                -Math.Abs(
                    amount);
        }

        amount =
            decimal.Round(
                amount,
                2,
                MidpointRounding.AwayFromZero);

        return true;
    }

    private static bool IsSectionTerminator(
        string normalizedLine)
    {
        return
            normalizedLine.StartsWith(
                "total charges",
                StringComparison.OrdinalIgnoreCase) ||

            normalizedLine.StartsWith(
                "total current charges",
                StringComparison.OrdinalIgnoreCase) ||

            normalizedLine.StartsWith(
                "total monthly charges",
                StringComparison.OrdinalIgnoreCase) ||

            normalizedLine.StartsWith(
                "total amount due",
                StringComparison.OrdinalIgnoreCase) ||

            normalizedLine.StartsWith(
                "amount due",
                StringComparison.OrdinalIgnoreCase) ||

            normalizedLine.StartsWith(
                "balance due",
                StringComparison.OrdinalIgnoreCase) ||

            normalizedLine.StartsWith(
                "new balance",
                StringComparison.OrdinalIgnoreCase) ||

            normalizedLine.StartsWith(
                "current balance",
                StringComparison.OrdinalIgnoreCase) ||

            normalizedLine.StartsWith(
                "account summary",
                StringComparison.OrdinalIgnoreCase) ||

            normalizedLine.StartsWith(
                "payment summary",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredDescription(
        string description)
    {
        var normalized =
            NormalizeLabel(
                description);

        return
            normalized.StartsWith(
                "previous balance",
                StringComparison.OrdinalIgnoreCase) ||

            normalized.StartsWith(
                "balance forward",
                StringComparison.OrdinalIgnoreCase) ||

            normalized.StartsWith(
                "past due",
                StringComparison.OrdinalIgnoreCase) ||

            normalized.StartsWith(
                "payment received",
                StringComparison.OrdinalIgnoreCase) ||

            normalized.Equals(
                "payment",
                StringComparison.OrdinalIgnoreCase) ||

            normalized.Equals(
                "payments",
                StringComparison.OrdinalIgnoreCase) ||

            normalized.StartsWith(
                "subtotal",
                StringComparison.OrdinalIgnoreCase) ||

            normalized.StartsWith(
                "total amount due",
                StringComparison.OrdinalIgnoreCase) ||

            normalized.StartsWith(
                "amount due",
                StringComparison.OrdinalIgnoreCase) ||

            normalized.StartsWith(
                "balance due",
                StringComparison.OrdinalIgnoreCase) ||

            normalized.StartsWith(
                "statement date",
                StringComparison.OrdinalIgnoreCase) ||

            normalized.StartsWith(
                "due date",
                StringComparison.OrdinalIgnoreCase) ||

            normalized.StartsWith(
                "billing period",
                StringComparison.OrdinalIgnoreCase) ||

            normalized.StartsWith(
                "service period",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string? ClassifyCategory(
        string description)
    {
        if (description.Contains(
                "tax",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Tax";
        }

        if (description.Contains(
                "fee",
                StringComparison.OrdinalIgnoreCase) ||
            description.Contains(
                "surcharge",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Fee";
        }

        if (description.Contains(
                "discount",
                StringComparison.OrdinalIgnoreCase) ||
            description.Contains(
                "promotion",
                StringComparison.OrdinalIgnoreCase) ||
            description.Contains(
                "promo",
                StringComparison.OrdinalIgnoreCase) ||
            description.Contains(
                "credit",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Discount";
        }

        if (description.Contains(
                "equipment",
                StringComparison.OrdinalIgnoreCase) ||
            description.Contains(
                "modem",
                StringComparison.OrdinalIgnoreCase) ||
            description.Contains(
                "router",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Equipment";
        }

        if (description.Contains(
                "service",
                StringComparison.OrdinalIgnoreCase) ||
            description.Contains(
                "plan",
                StringComparison.OrdinalIgnoreCase) ||
            description.Contains(
                "internet",
                StringComparison.OrdinalIgnoreCase) ||
            description.Contains(
                "phone",
                StringComparison.OrdinalIgnoreCase) ||
            description.Contains(
                "mobile",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Service";
        }

        return null;
    }

    private static string NormalizeLabel(
        string value)
    {
        return Regex.Replace(
                value
                    .Trim()
                    .TrimEnd(':'),
                @"\s+",
                " ")
            .ToLowerInvariant();
    }

    [GeneratedRegex(
        @"^\s*(?<description>.{2,200}?)\s+(?<amount>\(?\s*(?:-\s*)?\$?\s*-?\s*(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d{1,2})?\s*\)?)\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex
        LineItemRegex();
}

public sealed record BillStatementStructuredLineItem(
    string Description,
    decimal Amount,
    string? Category);