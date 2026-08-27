namespace BillWatch.API.Services.Statements;

public sealed class BillStatementDocumentTextReader
{
    private readonly SecureBillStatementStorageService _storageService;
    private readonly PdfBillStatementTextExtractor _pdfTextExtractor;

    public BillStatementDocumentTextReader(
        SecureBillStatementStorageService storageService,
        PdfBillStatementTextExtractor pdfTextExtractor)
    {
        _storageService =
            storageService;

        _pdfTextExtractor =
            pdfTextExtractor;
    }

    public BillStatementTextExtractionResult Read(
        Guid userId,
        string storageKey,
        string mediaType,
        string fileExtension)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(
                storageKey))
        {
            throw new ArgumentException(
                "Storage key is required.",
                nameof(storageKey));
        }

        if (string.IsNullOrWhiteSpace(
                mediaType))
        {
            throw new ArgumentException(
                "Media type is required.",
                nameof(mediaType));
        }

        if (string.IsNullOrWhiteSpace(
                fileExtension))
        {
            throw new ArgumentException(
                "File extension is required.",
                nameof(fileExtension));
        }

        var isPdf =
            IsPdf(
                mediaType,
                fileExtension);

        var isImage =
            IsImage(
                mediaType,
                fileExtension);

        if (!isPdf &&
            !isImage)
        {
            throw new BillStatementTextExtractionException(
                "The stored bill statement has an unsupported document type.");
        }

        using var statementStream =
            _storageService.OpenRead(
                userId,
                storageKey);

        if (isImage)
        {
            return new BillStatementTextExtractionResult(
                Text:
                    string.Empty,

                PageCount:
                    1,

                RequiresOcr:
                    true);
        }

        return _pdfTextExtractor.Extract(
            statementStream);
    }

    private static bool IsPdf(
        string mediaType,
        string fileExtension)
    {
        return
            string.Equals(
                mediaType,
                "application/pdf",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                fileExtension,
                ".pdf",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImage(
        string mediaType,
        string fileExtension)
    {
        var isJpeg =
            string.Equals(
                mediaType,
                "image/jpeg",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                fileExtension,
                ".jpg",
                StringComparison.OrdinalIgnoreCase);

        var isPng =
            string.Equals(
                mediaType,
                "image/png",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                fileExtension,
                ".png",
                StringComparison.OrdinalIgnoreCase);

        return
            isJpeg ||
            isPng;
    }
}