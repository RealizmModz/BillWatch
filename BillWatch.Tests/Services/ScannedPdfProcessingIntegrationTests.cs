using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Statements;
using BillWatch.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BillWatch.Tests.Services;

public sealed class ScannedPdfProcessingIntegrationTests
    : IClassFixture<BillWatchApiFactory>
{
    private static readonly TimeSpan ProcessingTimeout =
        TimeSpan.FromSeconds(5);

    private readonly BillWatchApiFactory
        _factory;

    public ScannedPdfProcessingIntegrationTests(
        BillWatchApiFactory factory)
    {
        _factory =
            factory;
    }

    [Fact]
    public async Task
        ImageOnlyPdf_OcrResultParsesPersistsAndCompletes()
    {
        /*
         * Native Tesseract PDF extraction has its own dedicated smoke
         * test.
         *
         * This test intentionally replaces OCR with a deterministic
         * implementation so the remainder of the scanned-PDF pipeline
         * stays fast and stable during routine development.
         */
        using var factory =
            _factory.WithWebHostBuilder(
                builder =>
                {
                    builder.ConfigureServices(
                        services =>
                        {
                            services.RemoveAll<
                                IBillStatementOcrEngine>();

                            services.AddSingleton<
                                IBillStatementOcrEngine,
                                DeterministicPdfOcrEngine>();
                        });
                });

        using var client =
            factory.CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    BaseAddress =
                        new Uri(
                            "https://localhost"),

                    AllowAutoRedirect =
                        false
                });

        var user =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        TestUserAuthentication.Authorize(
            client,
            user);

        var billStreamId =
            await CreateBillStreamAsync(
                client);

        var uploadId =
            await UploadPdfAsync(
                client,
                billStreamId,
                CreateImageOnlyPdf());

        await WaitForProcessedAsync(
            factory.Services,
            uploadId);

        using var scope =
            factory.Services
                .CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    BillWatchDbContext>();

        var upload =
            await dbContext.BillStatementUploads
                .AsNoTracking()
                .SingleAsync(
                    item =>
                        item.Id ==
                        uploadId);

        Assert.Equal(
            BillStatementUploadStatus.Processed,
            upload.Status);

        Assert.True(
            upload.BillStatementId.HasValue);

        var statement =
            await dbContext.BillStatements
                .AsNoTracking()
                .SingleAsync(
                    item =>
                        item.Id ==
                        upload.BillStatementId.Value);

        Assert.Equal(
            billStreamId,
            statement.BillStreamId);

        Assert.Equal(
            new DateOnly(
                2026,
                7,
                10),
            statement.PeriodStart);

        Assert.Equal(
            new DateOnly(
                2026,
                8,
                9),
            statement.PeriodEnd);

        Assert.Equal(
            new DateOnly(
                2026,
                8,
                10),
            statement.StatementDate);

        Assert.Equal(
            new DateOnly(
                2026,
                8,
                31),
            statement.DueDate);

        Assert.Equal(
            94.99m,
            statement.TotalAmount);

        Assert.Equal(
            "USD",
            statement.CurrencyCode);

        var statementCount =
            await dbContext.BillStatements
                .AsNoTracking()
                .CountAsync(
                    item =>
                        item.BillStreamId ==
                        billStreamId);

        Assert.Equal(
            1,
            statementCount);
    }

    private static async Task<Guid>
        CreateBillStreamAsync(
            HttpClient client)
    {
        using var response =
            await client.PostAsJsonAsync(
                "/api/bill-streams",
                new
                {
                    providerName =
                        $"Scanned PDF Test {Guid.NewGuid():N}",

                    category =
                        "Internet"
                });

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content
                .ReadFromJsonAsync<
                    BillStreamPayload>();

        return result?.Id
            ?? throw new InvalidOperationException(
                "The bill stream response was empty.");
    }

    private static async Task<Guid>
        UploadPdfAsync(
            HttpClient client,
            Guid billStreamId,
            byte[] pdfBytes)
    {
        using var multipart =
            new MultipartFormDataContent();

        using var fileContent =
            new ByteArrayContent(
                pdfBytes);

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue(
                "application/pdf");

        multipart.Add(
            fileContent,
            "file",
            "scanned-statement.pdf");

        using var response =
            await client.PostAsync(
                $"/api/bill-streams/{billStreamId}/statement-uploads",
                multipart);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var result =
            await response.Content
                .ReadFromJsonAsync<
                    StatementUploadPayload>();

        return result?.Id
            ?? throw new InvalidOperationException(
                "The statement upload response was empty.");
    }

    private static async Task WaitForProcessedAsync(
        IServiceProvider services,
        Guid uploadId)
    {
        var deadline =
            DateTimeOffset.UtcNow +
            ProcessingTimeout;

        BillStatementUploadStatus?
            lastStatus =
                null;

        while (DateTimeOffset.UtcNow <
               deadline)
        {
            using var scope =
                services.CreateScope();

            var dbContext =
                scope.ServiceProvider
                    .GetRequiredService<
                        BillWatchDbContext>();

            lastStatus =
                await dbContext.BillStatementUploads
                    .AsNoTracking()
                    .Where(
                        upload =>
                            upload.Id ==
                            uploadId)
                    .Select(
                        upload =>
                            (BillStatementUploadStatus?)
                            upload.Status)
                    .SingleOrDefaultAsync();

            if (lastStatus ==
                BillStatementUploadStatus.Processed)
            {
                return;
            }

            if (lastStatus is
                BillStatementUploadStatus.Failed or
                BillStatementUploadStatus.NeedsOcr or
                BillStatementUploadStatus.ReadyForParsing)
            {
                break;
            }

            await Task.Delay(
                25);
        }

        throw new TimeoutException(
            $"Scanned PDF upload {uploadId} did not reach Processed. Last status: {lastStatus?.ToString() ?? "not found"}.");
    }

    private static byte[] CreateImageOnlyPdf()
    {
        /*
         * A tiny valid image-only PDF.
         *
         * There is deliberately no PDF text layer, forcing
         * BillStatementDocumentTextReader down the OCR path.
         */
        var imageBytes =
            new byte[]
            {
                0xFF,
                0xD8,
                0xFF,
                0xD9
            };

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
               /Width 1
               /Height 1
               /ColorSpace /DeviceGray
               /BitsPerComponent 8
               /Filter /DCTDecode
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
            500 0 0 700 56 46 cm
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

    private sealed class DeterministicPdfOcrEngine
        : IBillStatementOcrEngine
    {
        private const string ExtractedStatementText =
            """
            BILLWATCH OCR TEST
            Statement Date: 08/10/2026
            Billing Period: 07/10/2026 - 08/09/2026
            Due Date: 08/31/2026
            Total Amount Due: $94.99
            """;

        public BillStatementOcrResult TryExtract(
            Stream source,
            string mediaType,
            string fileExtension)
        {
            ArgumentNullException.ThrowIfNull(
                source);

            if (!string.Equals(
                    mediaType,
                    "application/pdf",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    fileExtension,
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BillStatementOcrResult.Failure(
                    pageCount:
                        0);
            }

            return new BillStatementOcrResult(
                Text:
                    ExtractedStatementText,

                PageCount:
                    1,

                MeanConfidence:
                    0.99f,

                IsUsable:
                    true);
        }
    }

    private sealed class BillStreamPayload
    {
        public Guid Id { get; set; }
    }

    private sealed class StatementUploadPayload
    {
        public Guid Id { get; set; }
    }
}