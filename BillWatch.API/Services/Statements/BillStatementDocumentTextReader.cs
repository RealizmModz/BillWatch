namespace BillWatch.API.Services.Statements;

public sealed class BillStatementDocumentTextReader
{
    private readonly SecureBillStatementStorageService
        _storageService;

    private readonly PdfBillStatementTextExtractor
        _pdfTextExtractor;

    private readonly IBillStatementOcrEngine
        _ocrEngine;

    public BillStatementDocumentTextReader(
        SecureBillStatementStorageService storageService,
        PdfBillStatementTextExtractor pdfTextExtractor,
        IBillStatementOcrEngine ocrEngine)
    {
        _storageService =
            storageService;

        _pdfTextExtractor =
            pdfTextExtractor;

        _ocrEngine =
            ocrEngine;
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

        if (IsPdf(
                mediaType,
                fileExtension))
        {
            return ReadPdf(
                userId,
                storageKey,
                mediaType,
                fileExtension);
        }

        if (IsImage(
                mediaType,
                fileExtension))
        {
            return ReadImage(
                userId,
                storageKey,
                mediaType,
                fileExtension);
        }

        throw new BillStatementTextExtractionException(
            "The stored bill statement has an unsupported document type.");
    }

    private BillStatementTextExtractionResult ReadPdf(
        Guid userId,
        string storageKey,
        string mediaType,
        string fileExtension)
    {
        BillStatementTextExtractionResult
            pdfExtraction;

        /*
         * Fast path:
         *
         * A normal text PDF gets read only by PdfPig.
         * No native OCR initialization and no second file read.
         */
        using (var statementStream =
               _storageService.OpenRead(
                   userId,
                   storageKey))
        {
            pdfExtraction =
                _pdfTextExtractor.Extract(
                    statementStream);
        }

        if (!pdfExtraction.RequiresOcr)
        {
            return pdfExtraction;
        }

        /*
         * Sparse/scanned PDFs are reopened specifically for the OCR
         * fallback.
         */
        BillStatementOcrResult
            ocrResult;

        using (var statementStream =
               _storageService.OpenRead(
                   userId,
                   storageKey))
        {
            ocrResult =
                _ocrEngine.TryExtract(
                    statementStream,
                    mediaType,
                    fileExtension);
        }

        if (!ocrResult.IsUsable)
        {
            return new BillStatementTextExtractionResult(
                Text:
                    pdfExtraction.Text,

                PageCount:
                    Math.Max(
                        pdfExtraction.PageCount,
                        ocrResult.PageCount),

                RequiresOcr:
                    true);
        }

        return new BillStatementTextExtractionResult(
            Text:
                ocrResult.Text,

            PageCount:
                Math.Max(
                    pdfExtraction.PageCount,
                    ocrResult.PageCount),

            RequiresOcr:
                false);
    }

    private BillStatementTextExtractionResult ReadImage(
        Guid userId,
        string storageKey,
        string mediaType,
        string fileExtension)
    {
        using var statementStream =
            _storageService.OpenRead(
                userId,
                storageKey);

        var ocrResult =
            _ocrEngine.TryExtract(
                statementStream,
                mediaType,
                fileExtension);

        if (!ocrResult.IsUsable)
        {
            return new BillStatementTextExtractionResult(
                Text:
                    string.Empty,

                PageCount:
                    Math.Max(
                        1,
                        ocrResult.PageCount),

                RequiresOcr:
                    true);
        }

        return new BillStatementTextExtractionResult(
            Text:
                ocrResult.Text,

            PageCount:
                Math.Max(
                    1,
                    ocrResult.PageCount),

            RequiresOcr:
                false);
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
        var isPng =
            string.Equals(
                mediaType,
                "image/png",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                fileExtension,
                ".png",
                StringComparison.OrdinalIgnoreCase);

        var isJpeg =
            string.Equals(
                mediaType,
                "image/jpeg",
                StringComparison.OrdinalIgnoreCase) &&
            (
                string.Equals(
                    fileExtension,
                    ".jpg",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    fileExtension,
                    ".jpeg",
                    StringComparison.OrdinalIgnoreCase)
            );

        return
            isPng ||
            isJpeg;
    }
}