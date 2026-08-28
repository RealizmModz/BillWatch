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

public sealed class BillStatementProcessingPersistenceTests
    : IClassFixture<BillWatchApiFactory>
{
    private readonly BillWatchApiFactory
        _factory;

    public BillStatementProcessingPersistenceTests(
        BillWatchApiFactory factory)
    {
        _factory =
            factory;
    }

    [Fact]
    public async Task
        CompleteTextStatement_IsPersistedAndLinked()
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
                [
                    "MIDCO",
                    "Statement Date: 08/10/2026",
                    "Billing Period: 07/10/2026 - 08/09/2026",
                    "Due Date: 08/31/2026",
                    "Total Amount Due: $104.99"
                ]);

        var uploadId =
            await UploadAsync(
                client,
                billStreamId,
                pdfBytes);

        await WaitForStatusAsync(
            uploadId,
            BillStatementUploadStatus.Processed);

        using var scope =
            _factory.Services
                .CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    BillWatchDbContext>();

        var upload =
            await dbContext
                .BillStatementUploads
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
            await dbContext
                .BillStatements
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
            104.99m,
            statement.TotalAmount);

        Assert.Equal(
            "USD",
            statement.CurrencyCode);
    }

    [Fact]
    public async Task
        DuplicateStatementUpload_ReusesExistingStatement()
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
                [
                    "Black Hills Energy",
                    "Statement Date: 08/12/2026",
                    "Service Period: 07/01/2026 - 07/31/2026",
                    "Payment Due: 09/01/2026",
                    "Total Amount Due: $82.14"
                ]);

        var firstUploadId =
            await UploadAsync(
                client,
                billStreamId,
                pdfBytes);

        await WaitForStatusAsync(
            firstUploadId,
            BillStatementUploadStatus.Processed);

        var secondUploadId =
            await UploadAsync(
                client,
                billStreamId,
                pdfBytes);

        await WaitForStatusAsync(
            secondUploadId,
            BillStatementUploadStatus.Processed);

        using var scope =
            _factory.Services
                .CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    BillWatchDbContext>();

        var uploads =
            await dbContext
                .BillStatementUploads
                .AsNoTracking()
                .Where(
                    item =>
                        item.Id ==
                            firstUploadId ||
                        item.Id ==
                            secondUploadId)
                .ToListAsync();

        Assert.Equal(
            2,
            uploads.Count);

        Assert.All(
            uploads,
            upload =>
                Assert.Equal(
                    BillStatementUploadStatus.Processed,
                    upload.Status));

        Assert.All(
            uploads,
            upload =>
                Assert.True(
                    upload.BillStatementId.HasValue));

        Assert.Equal(
            uploads[0].BillStatementId,
            uploads[1].BillStatementId);

        var statementCount =
            await dbContext
                .BillStatements
                .AsNoTracking()
                .CountAsync(
                    statement =>
                        statement.BillStreamId ==
                        billStreamId);

        Assert.Equal(
            1,
            statementCount);
    }

    [Fact]
    public async Task
        IncompleteTextStatement_DoesNotInventStatementData()
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
                [
                    "Example Provider",
                    "Total Amount Due: $79.99",
                    "Thank you for being a valued customer.",
                    "Additional statement information is available online."
                ]);

        var uploadId =
            await UploadAsync(
                client,
                billStreamId,
                pdfBytes);

        await WaitForStatusAsync(
            uploadId,
            BillStatementUploadStatus.ReadyForParsing);

        using var scope =
            _factory.Services
                .CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    BillWatchDbContext>();

        var upload =
            await dbContext
                .BillStatementUploads
                .AsNoTracking()
                .SingleAsync(
                    item =>
                        item.Id ==
                        uploadId);

        Assert.Equal(
            BillStatementUploadStatus.ReadyForParsing,
            upload.Status);

        Assert.Null(
            upload.BillStatementId);

        var statementCount =
            await dbContext
                .BillStatements
                .AsNoTracking()
                .CountAsync(
                    statement =>
                        statement.BillStreamId ==
                        billStreamId);

        Assert.Equal(
            0,
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
                        $"Persistence Test {Guid.NewGuid():N}",

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
        UploadAsync(
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
            "statement.pdf");

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
                "The upload response was empty.");
    }

    private async Task WaitForStatusAsync(
        Guid uploadId,
        BillStatementUploadStatus expectedStatus)
    {
        var deadline =
            DateTimeOffset.UtcNow
                .AddSeconds(
                    5);

        BillStatementUploadStatus?
            lastStatus =
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
                await dbContext
                    .BillStatementUploads
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
                return;
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
        IReadOnlyList<string> lines)
    {
        var contentBuilder =
            new StringBuilder();

        contentBuilder.AppendLine(
            "BT");

        contentBuilder.AppendLine(
            "/F1 12 Tf");

        contentBuilder.AppendLine(
            "72 720 Td");

        for (var index = 0;
             index < lines.Count;
             index++)
        {
            if (index >
                0)
            {
                contentBuilder.AppendLine(
                    "0 -18 Td");
            }

            contentBuilder.Append(
                '(');

            contentBuilder.Append(
                EscapePdfText(
                    lines[index]));

            contentBuilder.AppendLine(
                ") Tj");
        }

        contentBuilder.AppendLine(
            "ET");

        var contentStream =
            contentBuilder.ToString();

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
                $"{offsets[index]:D10} 00000 n \n");
        }

        WriteAscii(
            output,
            $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{crossReferenceOffset}\n%%EOF\n");

        return output.ToArray();
    }

    private static string EscapePdfText(
        string value)
    {
        return value
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