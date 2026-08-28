using TesseractOCR;
using TesseractOCR.Enums;

namespace BillWatch.Tests.Services;

public sealed class TesseractNativeSmokeTests
{
    [Fact]
    public void EnglishEngine_InitializesWithPackagedModel()
    {
        var tessDataPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "tessdata");

        var englishModelPath =
            Path.Combine(
                tessDataPath,
                "eng.traineddata");

        Assert.True(
            File.Exists(
                englishModelPath),
            $"The packaged English OCR model was not found at '{englishModelPath}'.");

        var modelInfo =
            new FileInfo(
                englishModelPath);

        Assert.True(
            modelInfo.Length >
                0,
            "The packaged English OCR model is empty.");

        /*
         * Construction is the smoke test.
         *
         * It proves that:
         *
         * - the native Tesseract library can load,
         * - its native dependencies can load,
         * - the English trained-data model is available,
         * - Tesseract accepts that model.
         *
         * No customer document or statement data is involved.
         */
        using var engine =
            new Engine(
                tessDataPath,
                Language.English,
                EngineMode.Default);
    }
}