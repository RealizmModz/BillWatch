using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace BillWatch.API.Services.Statements;

/*
 * Bounded loader for an offline-only, private ground-truth corpus.
 *
 * The returned case contains sensitive statement data in memory. Callers
 * must never log it, persist it through the BillWatch database, or expose it
 * through an API response. This loader is intentionally not registered.
 */
public sealed class BillStatementAiPrivateCorpusLoader
{
    private const int MaxStatementCharacters =
        200_000;

    private const int MaxGroundTruthCharacters =
        64_000;

    private const int MaxProviderKeyLength =
        100;

    private const int MaxLineItems =
        100;

    private const int MaxLineItemDescriptionLength =
        200;

    private const int MaxLineItemCategoryLength =
        50;

    private const decimal MaxAbsoluteMoneyValue =
        1_000_000m;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive =
                false,
            UnmappedMemberHandling =
                JsonUnmappedMemberHandling.Disallow
        };

    public async Task<BillStatementAiPrivateCorpusCase> LoadAsync(
        string corpusRootDirectory,
        string caseId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await LoadCoreAsync(
                corpusRootDirectory,
                caseId,
                cancellationToken);
        }
        catch (BillStatementAiPrivateCorpusException)
        {
            throw;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus files could not be read.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus files could not be read.");
        }
        catch (DecoderFallbackException)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus contains invalid text encoding.");
        }
    }

    private static async Task<BillStatementAiPrivateCorpusCase> LoadCoreAsync(
        string corpusRootDirectory,
        string caseId,
        CancellationToken cancellationToken)
    {
        var statementPath =
            BillStatementAiPrivateCorpusPathPolicy
                .ResolveStatementTextPath(
                    corpusRootDirectory,
                    caseId);

        var groundTruthPath =
            BillStatementAiPrivateCorpusPathPolicy
                .ResolveGroundTruthPath(
                    corpusRootDirectory,
                    caseId);

        ValidateCaseDirectory(
            Path.GetDirectoryName(
                statementPath));

        var statementText =
            await ReadBoundedTextAsync(
                statementPath,
                MaxStatementCharacters,
                "statement text",
                cancellationToken);

        if (string.IsNullOrWhiteSpace(
                statementText))
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus statement text is empty.");
        }

        var groundTruthJson =
            await ReadBoundedTextAsync(
                groundTruthPath,
                MaxGroundTruthCharacters,
                "ground truth",
                cancellationToken);

        BillStatementAiGroundTruthDocument document;

        try
        {
            document =
                JsonSerializer.Deserialize<
                    BillStatementAiGroundTruthDocument>(
                    groundTruthJson,
                    JsonOptions)
                ?? throw new JsonException(
                    "The ground-truth document was empty.");
        }
        catch (JsonException)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus ground truth is invalid.");
        }

        ValidateGroundTruth(
            document);

        var expectedStatement =
            CreateExpectedStatement(
                document);

        var expectedLineItems =
            document.LineItems!
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
                                NormalizeOptionalString(
                                    item.Category)))
                .ToList()
                .AsReadOnly();

        return new BillStatementAiPrivateCorpusCase(
            CaseId:
                caseId,
            ProviderKey:
                document.ProviderKey.Trim(),
            StatementText:
                statementText,
            ExpectedStatement:
                expectedStatement,
            ExpectedLineItems:
                expectedLineItems);
    }

    private static async Task<string> ReadBoundedTextAsync(
        string path,
        int maximumCharacters,
        string contentName,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(
                path))
        {
            throw new BillStatementAiPrivateCorpusException(
                $"The private corpus {contentName} file is missing.");
        }

        var attributes =
            File.GetAttributes(
                path);

        if ((attributes &
                FileAttributes.ReparsePoint) !=
            0)
        {
            throw new BillStatementAiPrivateCorpusException(
                $"The private corpus {contentName} file cannot be a link or reparse point.");
        }

        await using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize:
                    4_096,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);

        if (stream.Length >
            maximumCharacters *
            4L)
        {
            throw new BillStatementAiPrivateCorpusException(
                $"The private corpus {contentName} file is too large.");
        }

        using var reader =
            new StreamReader(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        false,
                    throwOnInvalidBytes:
                        true),
                detectEncodingFromByteOrderMarks:
                    true);

        var content =
            await reader.ReadToEndAsync(
                cancellationToken);

        if (content.Length >
            maximumCharacters)
        {
            throw new BillStatementAiPrivateCorpusException(
                $"The private corpus {contentName} file is too large.");
        }

        return content;
    }

    private static void ValidateCaseDirectory(
        string? caseDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                caseDirectory) ||
            !Directory.Exists(
                caseDirectory))
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus case directory is missing.");
        }

        var attributes =
            File.GetAttributes(
                caseDirectory);

        if ((attributes &
                FileAttributes.ReparsePoint) !=
            0)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus case directory cannot be a link or reparse point.");
        }
    }

    private static void ValidateGroundTruth(
        BillStatementAiGroundTruthDocument document)
    {
        if (string.IsNullOrWhiteSpace(
                document.ProviderKey) ||
            document.ProviderKey.Trim().Length >
                MaxProviderKeyLength)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus provider key is invalid.");
        }

        if (document.BillingPeriodStart.HasValue &&
            document.BillingPeriodEnd.HasValue &&
            document.BillingPeriodStart.Value >
                document.BillingPeriodEnd.Value)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus billing period is invalid.");
        }

        ValidateMoney(
            document.TotalAmount,
            "total amount");

        if (!string.IsNullOrWhiteSpace(
                document.CurrencyCode) &&
            (document.CurrencyCode.Trim().Length !=
                3 ||
                document.CurrencyCode.Trim().Any(
                    character =>
                        !char.IsLetter(
                            character))))
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus currency code is invalid.");
        }

        if (document.LineItems is null ||
            document.LineItems.Count >
                MaxLineItems)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus line-item collection is invalid.");
        }

        foreach (var item in
                 document.LineItems)
        {
            if (item is null ||
                string.IsNullOrWhiteSpace(
                    item.Description) ||
                item.Description.Trim().Length >
                    MaxLineItemDescriptionLength ||
                item.Category?.Trim().Length >
                    MaxLineItemCategoryLength)
            {
                throw new BillStatementAiPrivateCorpusException(
                    "A private corpus line item is invalid.");
            }

            ValidateMoney(
                item.Amount,
                "line-item amount");
        }

        var containsScoredFact =
            document.TotalAmount.HasValue ||
            document.BillingPeriodStart.HasValue ||
            document.BillingPeriodEnd.HasValue ||
            document.StatementDate.HasValue ||
            document.DueDate.HasValue ||
            !string.IsNullOrWhiteSpace(
                document.CurrencyCode) ||
            document.LineItems.Count >
                0;

        if (!containsScoredFact)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus ground truth contains no scored facts.");
        }
    }

    private static void ValidateMoney(
        decimal? value,
        string fieldName)
    {
        if (value.HasValue &&
            (value.Value >
                MaxAbsoluteMoneyValue ||
                value.Value <
                -MaxAbsoluteMoneyValue))
        {
            throw new BillStatementAiPrivateCorpusException(
                $"The private corpus {fieldName} is outside the supported range.");
        }

        if (value.HasValue &&
            decimal.Round(
                value.Value,
                2,
                MidpointRounding.AwayFromZero) !=
            value.Value)
        {
            throw new BillStatementAiPrivateCorpusException(
                $"The private corpus {fieldName} exceeds cent precision.");
        }
    }

    private static BillStatementStructuredData CreateExpectedStatement(
        BillStatementAiGroundTruthDocument document)
    {
        var missing =
            new List<string>();

        if (!document.TotalAmount.HasValue)
        {
            missing.Add(
                nameof(
                    BillStatementStructuredData.TotalAmount));
        }

        if (!document.BillingPeriodStart.HasValue)
        {
            missing.Add(
                nameof(
                    BillStatementStructuredData.BillingPeriodStart));
        }

        if (!document.BillingPeriodEnd.HasValue)
        {
            missing.Add(
                nameof(
                    BillStatementStructuredData.BillingPeriodEnd));
        }

        if (string.IsNullOrWhiteSpace(
                document.CurrencyCode))
        {
            missing.Add(
                nameof(
                    BillStatementStructuredData.CurrencyCode));
        }

        return new BillStatementStructuredData(
            TotalAmount:
                document.TotalAmount,
            BillingPeriodStart:
                document.BillingPeriodStart,
            BillingPeriodEnd:
                document.BillingPeriodEnd,
            StatementDate:
                document.StatementDate,
            DueDate:
                document.DueDate,
            CurrencyCode:
                NormalizeOptionalString(
                    document.CurrencyCode) ??
                string.Empty,
            Confidence:
                missing.Count ==
                    0
                    ? BillStatementStructuredDataConfidence.StrongEvidence
                    : BillStatementStructuredDataConfidence.Partial,
            MissingRequiredFields:
                missing.AsReadOnly());
    }

    private static string? NormalizeOptionalString(
        string? value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? null
            : value
                .Trim()
                .ToUpperInvariant();
    }
}

public sealed record BillStatementAiPrivateCorpusCase(
    string CaseId,
    string ProviderKey,
    string StatementText,
    BillStatementStructuredData ExpectedStatement,
    IReadOnlyList<BillStatementStructuredLineItem> ExpectedLineItems);

public sealed record BillStatementAiGroundTruthDocument(
    string ProviderKey,
    decimal? TotalAmount,
    DateOnly? BillingPeriodStart,
    DateOnly? BillingPeriodEnd,
    DateOnly? StatementDate,
    DateOnly? DueDate,
    string? CurrencyCode,
    IReadOnlyList<BillStatementAiGroundTruthLineItem>? LineItems);

public sealed record BillStatementAiGroundTruthLineItem(
    string Description,
    decimal Amount,
    string? Category);

public sealed class BillStatementAiPrivateCorpusException
    : Exception
{
    public BillStatementAiPrivateCorpusException(
        string message)
        : base(message)
    {
    }

}
