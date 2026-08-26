using System.Text;
using System.Text.RegularExpressions;

namespace BillWatch.Core.Services;

public sealed class BillMerchantNormalizer
{
    private static readonly HashSet<string> NoiseWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ACH",
            "AUTOPAY",
            "AUTO",
            "PAY",
            "PAYMENT",
            "ONLINE",
            "WEB",
            "DEBIT",
            "CARD",
            "POS"
        };

    public string Normalize(string? merchantName)
    {
        if (string.IsNullOrWhiteSpace(merchantName))
        {
            return string.Empty;
        }

        var cleaned =
            ReplacePunctuationWithSpaces(
                merchantName.Trim());

        var parts =
            Regex.Split(
                    cleaned,
                    @"\s+")
                .Where(part =>
                    !string.IsNullOrWhiteSpace(part))
                .Where(part =>
                    !NoiseWords.Contains(part))
                .Where(part =>
                    !IsReferenceNumber(part))
                .Select(part =>
                    part.ToUpperInvariant())
                .ToList();

        return string.Join(
            ' ',
            parts);
    }

    private static string ReplacePunctuationWithSpaces(
        string value)
    {
        var builder =
            new StringBuilder(
                value.Length);

        foreach (var character in value)
        {
            builder.Append(
                char.IsLetterOrDigit(character) ||
                character == '&'
                    ? character
                    : ' ');
        }

        return builder.ToString();
    }

    private static bool IsReferenceNumber(
        string value)
    {
        return value.Length >= 4 &&
               value.All(char.IsDigit);
    }
}