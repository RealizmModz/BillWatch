using System.Text;
using UglyToad.PdfPig;

namespace BillWatch.Tests.Services;

public sealed class TesseractScannedPdfSmokeTests
{
    [Fact]
    public void ImageOnlyPdf_ExtractsEmbeddedPageImage()
    {
        var pdfBytes =
            CreateImageOnlyPdf();

        using var stream =
            new MemoryStream(
                pdfBytes);

        using var document =
            PdfDocument.Open(
                stream);

        var page =
            document.GetPage(
                1);

        var images =
            page.GetImages()
                .ToList();

        var image =
            Assert.Single(
                images);

        Assert.Equal(
            64,
            image.WidthInSamples);

        Assert.Equal(
            64,
            image.HeightInSamples);

        Assert.True(
            image.TryGetPng(
                out var pngBytes));

        Assert.NotNull(
            pngBytes);

        Assert.NotEmpty(
            pngBytes);
    }

    private static byte[] CreateImageOnlyPdf()
    {
        const int width =
            64;

        const int height =
            64;

        var imageBytes =
            new byte[
                width *
                height];

        /*
         * Simple grayscale checkerboard.
         *
         * We are testing the PDF -> raster-image bridge here.
         *
         * Native Tesseract recognition is already covered by
         * TesseractEndToEndOcrTests, so there is no reason to carry
         * a huge fragile Base64 JPEG inside this test.
         */
        for (var y = 0;
             y < height;
             y++)
        {
            for (var x = 0;
                 x < width;
                 x++)
            {
                var isDark =
                    ((x / 8) +
                     (y / 8)) %
                    2 ==
                    0;

                imageBytes[
                    (y * width) +
                    x] =
                    isDark
                        ? (byte)32
                        : (byte)224;
            }
        }

        using var output =
            new MemoryStream();

        var offsets =
            new long[6];

        WriteAscii(
            output,
            "%PDF-1.4\n");

        offsets[1] =
            output.Position;

        WriteAscii(
            output,
            """
            1 0 obj
            << /Type /Catalog /Pages 2 0 R >>
            endobj

            """);

        offsets[2] =
            output.Position;

        WriteAscii(
            output,
            """
            2 0 obj
            << /Type /Pages /Kids [3 0 R] /Count 1 >>
            endobj

            """);

        offsets[3] =
            output.Position;

        WriteAscii(
            output,
            """
            3 0 obj
            <<
              /Type /Page
              /Parent 2 0 R
              /MediaBox [0 0 612 792]
              /Resources <<
                /XObject <<
                  /Im0 4 0 R
                >>
              >>
              /Contents 5 0 R
            >>
            endobj

            """);

        offsets[4] =
            output.Position;

        WriteAscii(
            output,
            $"""
             4 0 obj
             <<
               /Type /XObject
               /Subtype /Image
               /Width {width}
               /Height {height}
               /ColorSpace /DeviceGray
               /BitsPerComponent 8
               /Length {imageBytes.Length}
             >>
             stream

             """);

        output.Write(
            imageBytes,
            0,
            imageBytes.Length);

        WriteAscii(
            output,
            """

            endstream
            endobj

            """);

        const string pageContent =
            """
            q
            400 0 0 400 106 196 cm
            /Im0 Do
            Q
            """;

        var pageContentBytes =
            Encoding.ASCII.GetBytes(
                pageContent);

        offsets[5] =
            output.Position;

        WriteAscii(
            output,
            $"""
             5 0 obj
             << /Length {pageContentBytes.Length} >>
             stream
             {pageContent}
             endstream
             endobj

             """);

        var xrefOffset =
            output.Position;

        WriteAscii(
            output,
            """
            xref
            0 6
            0000000000 65535 f 
            """);

        for (var objectNumber = 1;
             objectNumber <= 5;
             objectNumber++)
        {
            WriteAscii(
                output,
                $"{offsets[objectNumber]:D10} 00000 n \n");
        }

        WriteAscii(
            output,
            $"""
             trailer
             << /Size 6 /Root 1 0 R >>
             startxref
             {xrefOffset}
             %%EOF
             """);

        return output.ToArray();
    }

    private static void WriteAscii(
        Stream destination,
        string value)
    {
        var bytes =
            Encoding.ASCII.GetBytes(
                value);

        destination.Write(
            bytes,
            0,
            bytes.Length);
    }
}