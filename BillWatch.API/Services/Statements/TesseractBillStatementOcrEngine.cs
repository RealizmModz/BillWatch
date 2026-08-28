using System.Text;
using Microsoft.Extensions.Options;
using TesseractOCR;
using TesseractOCR.Enums;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace BillWatch.API.Services.Statements;

public interface IBillStatementOcrEngine
{
    BillStatementOcrResult TryExtract(
        Stream source,
        string mediaType,
        string fileExtension);
}

public sealed class TesseractBillStatementOcrEngine
    : IBillStatementOcrEngine,
      IDisposable
{
    private const int MaxPdfPages =
        100;

    private const int MaxImagesPerPdfPage =
        4;

    private const long MinimumPdfImagePixels =
        50_000;

    private const long MaximumPdfImagePixels =
        50_000_000;

    private const int MaxImageBytes =
        20 * 1024 * 1024;

    private const int MaxExtractedCharacters =
        250_000;

    private const int MinimumUsefulCharacters =
        40;

    private const float MinimumPerImageConfidence =
        0.50f;

    private readonly object _engineLock =
        new();

    private readonly string _tessDataPath;

    private readonly float _minimumMeanConfidence;

    private readonly ILogger<TesseractBillStatementOcrEngine>
        _logger;

    private Engine?
        _engine;

    private bool
        _engineInitializationAttempted;

    private bool
        _disposed;

    public TesseractBillStatementOcrEngine(
        IOptions<BillStatementOcrOptions> options,
        ILogger<TesseractBillStatementOcrEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(
            options);

        _logger =
            logger;

        var configuredOptions =
            options.Value;

        _minimumMeanConfidence =
            configuredOptions.MinimumMeanConfidence;

        if (_minimumMeanConfidence is
            < 0f or > 1f)
        {
            throw new InvalidOperationException(
                "BillStatementOcr:MinimumMeanConfidence must be between 0 and 1.");
        }

        _tessDataPath =
            string.IsNullOrWhiteSpace(
                configuredOptions.TessDataPath)
                ? Path.Combine(
                    AppContext.BaseDirectory,
                    "tessdata")
                : Path.GetFullPath(
                    configuredOptions.TessDataPath);
    }

    public BillStatementOcrResult TryExtract(
        Stream source,
        string mediaType,
        string fileExtension)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        ArgumentNullException.ThrowIfNull(
            source);

        if (!source.CanRead)
        {
            throw new ArgumentException(
                "The OCR source stream is not readable.",
                nameof(source));
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

        lock (_engineLock)
        {
            var engine =
                GetOrCreateEngine();

            if (engine is null)
            {
                return BillStatementOcrResult.Failure(
                    pageCount:
                        0);
            }

            try
            {
                if (IsPdf(
                        mediaType,
                        fileExtension))
                {
                    return ExtractPdf(
                        engine,
                        source);
                }

                if (IsImage(
                        mediaType,
                        fileExtension))
                {
                    return ExtractImage(
                        engine,
                        source);
                }

                return BillStatementOcrResult.Failure(
                    pageCount:
                        0);
            }
            catch (Exception ex)
            {
                /*
                 * OCR remains a best-effort secondary extraction path.
                 *
                 * Never log document text, storage paths, OCR output,
                 * or native exception messages.
                 */
                _logger.LogWarning(
                    "Local statement OCR failed with {ExceptionType}.",
                    ex.GetType().Name);

                return BillStatementOcrResult.Failure(
                    pageCount:
                        0);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_engineLock)
        {
            if (_disposed)
            {
                return;
            }

            _engine?.Dispose();

            _engine =
                null;

            _disposed =
                true;
        }
    }

    private Engine? GetOrCreateEngine()
    {
        if (_engine is not null)
        {
            return _engine;
        }

        if (_engineInitializationAttempted)
        {
            return null;
        }

        _engineInitializationAttempted =
            true;

        var englishModelPath =
            Path.Combine(
                _tessDataPath,
                "eng.traineddata");

        if (!File.Exists(
                englishModelPath))
        {
            _logger.LogError(
                "Local statement OCR could not start because the English OCR model is unavailable.");

            return null;
        }

        try
        {
            /*
             * Tesseract is deliberately initialized lazily.
             *
             * Text-based PDFs never pay this startup or memory cost.
             */
            _engine =
                new Engine(
                    _tessDataPath,
                    Language.English,
                    EngineMode.Default);

            return _engine;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Local statement OCR engine initialization failed with {ExceptionType}.",
                ex.GetType().Name);

            return null;
        }
    }

    private BillStatementOcrResult ExtractImage(
        Engine engine,
        Stream source)
    {
        var bytes =
            ReadStreamWithLimit(
                source,
                MaxImageBytes);

        if (bytes.Length ==
            0)
        {
            return BillStatementOcrResult.Failure(
                pageCount:
                    1);
        }

        if (!TryRecognizeImage(
                engine,
                bytes,
                out var text,
                out var confidence))
        {
            return BillStatementOcrResult.Failure(
                pageCount:
                    1);
        }

        var normalizedText =
            NormalizeAndLimit(
                text);

        var isUsable =
            normalizedText.Length >=
                MinimumUsefulCharacters &&
            confidence >=
                _minimumMeanConfidence;

        return new BillStatementOcrResult(
            Text:
                normalizedText,

            PageCount:
                1,

            MeanConfidence:
                confidence,

            IsUsable:
                isUsable);
    }

    private BillStatementOcrResult ExtractPdf(
        Engine engine,
        Stream source)
    {
        if (source.CanSeek)
        {
            source.Position =
                0;
        }

        using var document =
            PdfDocument.Open(
                source);

        var textBuilder =
            new StringBuilder();

        double weightedConfidence =
            0d;

        long confidenceWeight =
            0;

        var pageCount =
            0;

        foreach (var page in
                 document.GetPages())
        {
            pageCount++;

            if (pageCount >
                MaxPdfPages)
            {
                return BillStatementOcrResult.Failure(
                    pageCount:
                        pageCount);
            }

            var candidates =
                page.GetImages()
                    .Select(
                        image =>
                            new PdfImageCandidate(
                                image,
                                GetPixelCount(
                                    image)))
                    .Where(
                        candidate =>
                            candidate.PixelCount >=
                                MinimumPdfImagePixels &&
                            candidate.PixelCount <=
                                MaximumPdfImagePixels)
                    .OrderByDescending(
                        candidate =>
                            candidate.PixelCount)
                    .Take(
                        MaxImagesPerPdfPage)
                    .ToList();

            foreach (var candidate in
                     candidates)
            {
                var imageBytes =
                    GetPdfImageBytes(
                        candidate.Image);

                if (imageBytes is null ||
                    imageBytes.Length ==
                        0 ||
                    imageBytes.Length >
                        MaxImageBytes)
                {
                    continue;
                }

                if (!TryRecognizeImage(
                        engine,
                        imageBytes,
                        out var imageText,
                        out var imageConfidence))
                {
                    continue;
                }

                var normalizedImageText =
                    NormalizeAndLimit(
                        imageText);

                if (normalizedImageText.Length <
                        MinimumUsefulCharacters ||
                    imageConfidence <
                        MinimumPerImageConfidence)
                {
                    continue;
                }

                AppendTextWithLimit(
                    textBuilder,
                    normalizedImageText);

                weightedConfidence +=
                    imageConfidence *
                    normalizedImageText.Length;

                confidenceWeight +=
                    normalizedImageText.Length;

                if (textBuilder.Length >=
                    MaxExtractedCharacters)
                {
                    break;
                }
            }

            if (textBuilder.Length >=
                MaxExtractedCharacters)
            {
                break;
            }
        }

        if (textBuilder.Length ==
                0 ||
            confidenceWeight ==
                0)
        {
            return BillStatementOcrResult.Failure(
                pageCount:
                    pageCount);
        }

        var meanConfidence =
            (float)
            (weightedConfidence /
             confidenceWeight);

        meanConfidence =
            Math.Clamp(
                meanConfidence,
                0f,
                1f);

        var finalText =
            textBuilder
                .ToString()
                .Trim();

        var isUsable =
            finalText.Length >=
                MinimumUsefulCharacters &&
            meanConfidence >=
                _minimumMeanConfidence;

        return new BillStatementOcrResult(
            Text:
                finalText,

            PageCount:
                pageCount,

            MeanConfidence:
                meanConfidence,

            IsUsable:
                isUsable);
    }

    private bool TryRecognizeImage(
        Engine engine,
        byte[] imageBytes,
        out string text,
        out float confidence)
    {
        text =
            string.Empty;

        confidence =
            0f;

        try
        {
            using var image =
                TesseractOCR.Pix.Image
                    .LoadFromMemory(
                        imageBytes);

            using var page =
                engine.Process(
                    image);

            text =
                page.Text?
                    .Trim()
                ?? string.Empty;

            confidence =
                Math.Clamp(
                    page.MeanConfidence,
                    0f,
                    1f);

            return text.Length >
                0;
        }
        catch (Exception ex)
        {
            /*
             * A PDF can contain logos, masks, decorative graphics, and
             * unsupported image encodings.
             *
             * One bad image must not invalidate the entire statement.
             */
            _logger.LogDebug(
                "A statement image could not be OCR'd with {ExceptionType}.",
                ex.GetType().Name);

            return false;
        }
    }

    private static byte[]? GetPdfImageBytes(
        IPdfImage image)
    {
        if (image.TryGetPng(
                out var pngBytes) &&
            pngBytes is
            {
                Length: > 0
            })
        {
            return pngBytes;
        }

        /*
         * PdfPig exposes the original JPEG file in RawMemory for
         * JPEG-backed PDF images.
         *
         * Do not feed arbitrary decoded PDF bitmap bytes into
         * Tesseract as though they were standalone image files.
         */
        var rawMemory =
            image.RawMemory;

        if (rawMemory.Length <
            3)
        {
            return null;
        }

        var rawSpan =
            rawMemory.Span;

        var isJpeg =
            rawSpan[0] ==
                0xFF &&
            rawSpan[1] ==
                0xD8 &&
            rawSpan[2] ==
                0xFF;

        if (!isJpeg)
        {
            return null;
        }

        return rawMemory.ToArray();
    }

    private static long GetPixelCount(
        IPdfImage image)
    {
        var width =
            Math.Max(
                0,
                image.WidthInSamples);

        var height =
            Math.Max(
                0,
                image.HeightInSamples);

        return
            (long)width *
            height;
    }

    private static void AppendTextWithLimit(
        StringBuilder destination,
        string text)
    {
        if (destination.Length >=
            MaxExtractedCharacters)
        {
            return;
        }

        if (destination.Length >
            0)
        {
            destination.AppendLine();
            destination.AppendLine();
        }

        var remainingCharacters =
            MaxExtractedCharacters -
            destination.Length;

        if (remainingCharacters <=
            0)
        {
            return;
        }

        if (text.Length <=
            remainingCharacters)
        {
            destination.Append(
                text);

            return;
        }

        destination.Append(
            text.AsSpan(
                0,
                remainingCharacters));
    }

    private static byte[] ReadStreamWithLimit(
        Stream source,
        int maximumBytes)
    {
        if (source.CanSeek)
        {
            if (source.Length >
                maximumBytes)
            {
                return [];
            }

            source.Position =
                0;
        }

        using var destination =
            new MemoryStream();

        var buffer =
            new byte[81920];

        var totalBytes =
            0;

        while (true)
        {
            var bytesRead =
                source.Read(
                    buffer,
                    0,
                    buffer.Length);

            if (bytesRead ==
                0)
            {
                break;
            }

            totalBytes +=
                bytesRead;

            if (totalBytes >
                maximumBytes)
            {
                return [];
            }

            destination.Write(
                buffer,
                0,
                bytesRead);
        }

        return destination.ToArray();
    }

    private static string NormalizeAndLimit(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }

        var normalized =
            value
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n')
                .Trim();

        if (normalized.Length <=
            MaxExtractedCharacters)
        {
            return normalized;
        }

        return normalized[
            ..MaxExtractedCharacters];
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

    private sealed record PdfImageCandidate(
        IPdfImage Image,
        long PixelCount);
}

public sealed record BillStatementOcrResult(
    string Text,
    int PageCount,
    float MeanConfidence,
    bool IsUsable)
{
    public static BillStatementOcrResult Failure(
        int pageCount)
    {
        return new BillStatementOcrResult(
            Text:
                string.Empty,

            PageCount:
                pageCount,

            MeanConfidence:
                0f,

            IsUsable:
                false);
    }
}

public sealed class BillStatementOcrOptions
{
    public const string SectionName =
        "BillStatementOcr";

    public string? TessDataPath { get; set; }

    public float MinimumMeanConfidence { get; set; } =
        0.80f;
}