using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace BillWatch.API.Services.Statements;

public sealed class PdfBillStatementTextExtractor
{
    private const int MaxPages =
        100;

    private const int MaxExtractedCharacters =
        250_000;

    private const int MinimumUsefulCharacters =
        40;

    public BillStatementTextExtractionResult Extract(
        Stream pdfStream)
    {
        ArgumentNullException.ThrowIfNull(
            pdfStream);

        if (!pdfStream.CanRead)
        {
            throw new ArgumentException(
                "The PDF stream is not readable.",
                nameof(pdfStream));
        }

        try
        {
            using var document =
                PdfDocument.Open(
                    pdfStream);

            var textBuilder =
                new StringBuilder();

            var pageCount =
                0;

            foreach (var page in document.GetPages())
            {
                pageCount++;

                if (pageCount >
                    MaxPages)
                {
                    throw new BillStatementTextExtractionException(
                        $"The statement exceeds the {MaxPages}-page processing limit.");
                }

                var pageText =
                    ContentOrderTextExtractor.GetText(
                        page);

                if (string.IsNullOrWhiteSpace(
                        pageText))
                {
                    continue;
                }

                if (textBuilder.Length >
                    0)
                {
                    textBuilder.AppendLine();
                    textBuilder.AppendLine();
                }

                var remainingCharacters =
                    MaxExtractedCharacters -
                    textBuilder.Length;

                if (remainingCharacters <=
                    0)
                {
                    break;
                }

                if (pageText.Length >
                    remainingCharacters)
                {
                    textBuilder.Append(
                        pageText.AsSpan(
                            0,
                            remainingCharacters));

                    break;
                }

                textBuilder.Append(
                    pageText);
            }

            var extractedText =
                NormalizeText(
                    textBuilder.ToString());

            var requiresOcr =
                extractedText.Length <
                MinimumUsefulCharacters;

            return new BillStatementTextExtractionResult(
                Text:
                    extractedText,

                PageCount:
                    pageCount,

                RequiresOcr:
                    requiresOcr);
        }
        catch (BillStatementTextExtractionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BillStatementTextExtractionException(
                "BillWatch could not safely read this PDF statement.",
                ex);
        }
    }

    private static string NormalizeText(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }

        var lines =
            value
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n')
                .Split(
                    '\n');

        var normalized =
            new StringBuilder(
                Math.Min(
                    value.Length,
                    MaxExtractedCharacters));

        var hasContent =
            false;

        var pendingBlankLine =
            false;

        foreach (var rawLine in lines)
        {
            var line =
                rawLine.Trim();

            if (line.Length ==
                0)
            {
                if (hasContent)
                {
                    pendingBlankLine =
                        true;
                }

                continue;
            }

            if (hasContent)
            {
                normalized.AppendLine();

                if (pendingBlankLine)
                {
                    normalized.AppendLine();
                }
            }

            normalized.Append(
                line);

            hasContent =
                true;

            pendingBlankLine =
                false;
        }

        return normalized.ToString();
    }
}

public sealed record BillStatementTextExtractionResult(
    string Text,
    int PageCount,
    bool RequiresOcr);

public sealed class BillStatementTextExtractionException
    : Exception
{
    public BillStatementTextExtractionException(
        string message)
        : base(message)
    {
    }

    public BillStatementTextExtractionException(
        string message,
        Exception innerException)
        : base(
            message,
            innerException)
    {
    }
}