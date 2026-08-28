using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BillWatch.API.Data;
using BillWatch.API.Data.Entities;
using BillWatch.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillWatch.Tests.Services;

public sealed class BillStatementProcessingBackgroundServiceTests
    : IClassFixture<BillWatchApiFactory>
{
    private readonly BillWatchApiFactory _factory;

    public BillStatementProcessingBackgroundServiceTests(
        BillWatchApiFactory factory)
    {
        _factory =
            factory;
    }

    [Fact]
    public async Task
        UploadedTextPdf_BecomesReadyForParsing()
    {
        using var client =
            _factory.CreateHttpsClient();

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

        var pdfBytes =
            CreatePdf(
                "Midco internet statement total amount due $104.99 for the current monthly billing period.");

        var uploadId =
            await UploadAsync(
                client,
                billStreamId,
                pdfBytes,
                "statement.pdf",
                "application/pdf");

        var status =
            await WaitForStatusAsync(
                uploadId,
                BillStatementUploadStatus.ReadyForParsing);

        Assert.Equal(
            BillStatementUploadStatus.ReadyForParsing,
            status);
    }

    [Fact]
    public async Task
        UploadedImage_BecomesNeedsOcr()
    {
        using var client =
            _factory.CreateHttpsClient();

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

        var pngBytes =
            new byte[]
            {
                0x89,
                0x50,
                0x4E,
                0x47,
                0x0D,
                0x0A,
                0x1A,
                0x0A
            };

        var uploadId =
            await UploadAsync(
                client,
                billStreamId,
                pngBytes,
                "statement.png",
                "image/png");

        var status =
            await WaitForStatusAsync(
                uploadId,
                BillStatementUploadStatus.NeedsOcr);

        Assert.Equal(
            BillStatementUploadStatus.NeedsOcr,
            status);
    }

    private async Task<Guid> CreateBillStreamAsync(
        HttpClient client)
    {
        using var response =
            await client.PostAsJsonAsync(
                "/api/bill-streams",
                new
                {
                    providerName =
                        $"Processing Test {Guid.NewGuid():N}",

                    category =
                        "Internet"
                });

        response.EnsureSuccessStatusCode();

        var payload =
            await response.Content
                .ReadFromJsonAsync<
                    BillStreamPayload>();

        return payload?.Id
            ?? throw new InvalidOperationException(
                "The bill stream response was empty.");
    }

    private static async Task<Guid> UploadAsync(
        HttpClient client,
        Guid billStreamId,
        byte[] bytes,
        string fileName,
        string mediaType)
    {
        using var multipart =
            new MultipartFormDataContent();

        using var fileContent =
            new ByteArrayContent(
                bytes);

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue(
                mediaType);

        multipart.Add(
            fileContent,
            "file",
            fileName);

        using var response =
            await client.PostAsync(
                $"/api/bill-streams/{billStreamId}/statement-uploads",
                multipart);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var payload =
            await response.Content
                .ReadFromJsonAsync<
                    StatementUploadPayload>();

        return payload?.Id
            ?? throw new InvalidOperationException(
                "The statement upload response was empty.");
    }

    private async Task<BillStatementUploadStatus>
        WaitForStatusAsync(
            Guid uploadId,
            BillStatementUploadStatus expectedStatus)
    {
        var deadline =
            DateTimeOffset.UtcNow
                .AddSeconds(5);

        BillStatementUploadStatus? lastStatus =
            null;

        while (DateTimeOffset.UtcNow <
               deadline)
        {
            using var scope =
                _factory.Services
                    .CreateScope();

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
                expectedStatus)
            {
                return expectedStatus;
            }

            if (lastStatus ==
                BillStatementUploadStatus.Failed)
            {
                break;
            }

            await Task.Delay(
                25);
        }

        throw new TimeoutException(
            $"Statement upload {uploadId} did not reach {expectedStatus}. Last status: {lastStatus?.ToString() ?? "not found"}.");
    }

    private static byte[] CreatePdf(
        string text)
    {
        var escapedText =
            text
                .Replace(
                    "\\",
                    "\\\\",
                    StringComparison.Ordinal)
                .Replace(
                    "(",
                    "\\(",
                    StringComparison.Ordinal)
                .Replace(
                    ")",
                    "\\)",
                    StringComparison.Ordinal);

        var contentStream =
            $"BT\n/F1 12 Tf\n72 720 Td\n({escapedText}) Tj\nET\n";

        var objects =
            new[]
            {
                "<< /Type /Catalog /Pages 2 0 R >>",

                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",

                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",

                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",

                $"<< /Length {Encoding.ASCII.GetByteCount(contentStream)} >>\nstream\n{contentStream}endstream"
            };

        using var output =
            new MemoryStream();

        WriteAscii(
            output,
            "%PDF-1.4\n");

        var offsets =
            new List<long>
            {
                0
            };

        for (var index = 0;
             index < objects.Length;
             index++)
        {
            offsets.Add(
                output.Position);

            WriteAscii(
                output,
                $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var crossReferenceOffset =
            output.Position;

        WriteAscii(
            output,
            $"xref\n0 {objects.Length + 1}\n");

        WriteAscii(
            output,
            "0000000000 65535 f \n");

        for (var index = 1;
             index < offsets.Count;
             index++)
        {
            WriteAscii(
                output,
                $"{offsets[index].ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        }

        WriteAscii(
            output,
            $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{crossReferenceOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");

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

    private sealed class BillStreamPayload
    {
        public Guid Id { get; set; }
    }

    private sealed class StatementUploadPayload
    {
        public Guid Id { get; set; }
    }
}