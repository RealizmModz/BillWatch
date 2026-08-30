using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class BillStatementUploadStatusAuthorizationTests
    : IClassFixture<BillWatchApiFactory>
{
    private readonly BillWatchApiFactory _factory;

    public BillStatementUploadStatusAuthorizationTests(
        BillWatchApiFactory factory)
    {
        _factory =
            factory;
    }

    [Fact]
    public async Task
        Status_RequiresAuthentication()
    {
        using var client =
            _factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                $"/api/bill-streams/{Guid.NewGuid()}/statement-uploads/{Guid.NewGuid()}");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task
        Status_ReturnsOwnedUpload()
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

        var billStream =
            await CreateBillStreamAsync(
                client);

        var upload =
            await UploadStatementAsync(
                client,
                billStream.Id);

        using var response =
            await client.GetAsync(
                $"/api/bill-streams/{billStream.Id}/statement-uploads/{upload.Id}");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result =
            await response.Content
                .ReadFromJsonAsync<
                    BillStatementUploadStatusPayload>();

        Assert.NotNull(
            result);

        Assert.Equal(
            upload.Id,
            result.Id);

        Assert.Equal(
            billStream.Id,
            result.BillStreamId);

        Assert.False(
            string.IsNullOrWhiteSpace(
                result.Status));

        Assert.NotEqual(
            default,
            result.CreatedAtUtc);

        Assert.NotEqual(
            default,
            result.UpdatedAtUtc);

        var responseText =
            await response.Content
                .ReadAsStringAsync();

        Assert.DoesNotContain(
            "storageKey",
            responseText,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "physicalPath",
            responseText,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "text",
            responseText,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task
        Status_CannotReadAnotherUsersUpload()
    {
        using var client =
            _factory.CreateHttpsClient();

        var owner =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        var attacker =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        TestUserAuthentication.Authorize(
            client,
            owner);

        var billStream =
            await CreateBillStreamAsync(
                client);

        var upload =
            await UploadStatementAsync(
                client,
                billStream.Id);

        TestUserAuthentication.Authorize(
            client,
            attacker);

        using var response =
            await client.GetAsync(
                $"/api/bill-streams/{billStream.Id}/statement-uploads/{upload.Id}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task
        Status_ReturnsNotFoundWhenBillStreamDoesNotMatch()
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

        var billStream =
            await CreateBillStreamAsync(
                client);

        var otherBillStream =
            await CreateBillStreamAsync(
                client);

        var upload =
            await UploadStatementAsync(
                client,
                billStream.Id);

        using var response =
            await client.GetAsync(
                $"/api/bill-streams/{otherBillStream.Id}/statement-uploads/{upload.Id}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task
        File_RequiresAuthentication()
    {
        using var client =
            _factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                $"/api/bill-streams/{Guid.NewGuid()}/statement-uploads/{Guid.NewGuid()}/file");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task
        File_ReturnsOwnedStatementWithSafeDownloadName()
    {
        using var client =
            _factory.CreateHttpsClient();

        var user =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        TestUserAuthentication.Authorize(
            client,
            user);

        var billStream =
            await CreateBillStreamAsync(
                client);

        var upload =
            await UploadStatementAsync(
                client,
                billStream.Id);

        using var response =
            await client.GetAsync(
                $"/api/bill-streams/{billStream.Id}/statement-uploads/{upload.Id}/file");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        Assert.Equal(
            "application/pdf",
            response.Content.Headers.ContentType?.MediaType);

        Assert.Equal(
            $"billwatch-statement-{upload.Id:N}.pdf",
            response.Content.Headers.ContentDisposition?
                .FileName?
                .Trim('"'));

        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString() ??
            string.Empty,
            StringComparison.OrdinalIgnoreCase);

        var downloadedBytes =
            await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(
            CreatePdfBytes(),
            downloadedBytes);
    }

    [Fact]
    public async Task
        File_CannotReadAnotherUsersStatement()
    {
        using var client =
            _factory.CreateHttpsClient();

        var owner =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        var attacker =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        TestUserAuthentication.Authorize(
            client,
            owner);

        var billStream =
            await CreateBillStreamAsync(
                client);

        var upload =
            await UploadStatementAsync(
                client,
                billStream.Id);

        TestUserAuthentication.Authorize(
            client,
            attacker);

        using var response =
            await client.GetAsync(
                $"/api/bill-streams/{billStream.Id}/statement-uploads/{upload.Id}/file");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task
        File_ReturnsNotFoundWhenBillStreamDoesNotMatch()
    {
        using var client =
            _factory.CreateHttpsClient();

        var user =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        TestUserAuthentication.Authorize(
            client,
            user);

        var billStream =
            await CreateBillStreamAsync(
                client);

        var otherBillStream =
            await CreateBillStreamAsync(
                client);

        var upload =
            await UploadStatementAsync(
                client,
                billStream.Id);

        using var response =
            await client.GetAsync(
                $"/api/bill-streams/{otherBillStream.Id}/statement-uploads/{upload.Id}/file");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    private static async Task<BillStreamPayload>
        CreateBillStreamAsync(
            HttpClient client)
    {
        using var response =
            await client.PostAsJsonAsync(
                "/api/bill-streams",
                new
                {
                    providerName =
                        $"Status Test {Guid.NewGuid():N}",

                    category =
                        "Internet"
                });

        response.EnsureSuccessStatusCode();

        return await response.Content
                   .ReadFromJsonAsync<
                       BillStreamPayload>()
               ?? throw new InvalidOperationException(
                   "The bill stream response was empty.");
    }

    private static async Task<StatementUploadPayload>
        UploadStatementAsync(
            HttpClient client,
            Guid billStreamId)
    {
        using var content =
            CreateMultipartFile(
                CreatePdfBytes(),
                "statement.pdf",
                "application/pdf");

        using var response =
            await client.PostAsync(
                $"/api/bill-streams/{billStreamId}/statement-uploads",
                content);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        return await response.Content
                   .ReadFromJsonAsync<
                       StatementUploadPayload>()
               ?? throw new InvalidOperationException(
                   "The statement upload response was empty.");
    }

    private static MultipartFormDataContent
        CreateMultipartFile(
            byte[] bytes,
            string fileName,
            string mediaType)
    {
        var content =
            new MultipartFormDataContent();

        var fileContent =
            new ByteArrayContent(
                bytes);

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue(
                mediaType);

        content.Add(
            fileContent,
            "file",
            fileName);

        return content;
    }

    private static byte[] CreatePdfBytes()
    {
        const string contentStream =
            "BT\n/F1 12 Tf\n72 720 Td\n(BillWatch statement processing test with enough embedded text to avoid OCR.) Tj\nET\n";

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

    private sealed class BillStatementUploadStatusPayload
    {
        public Guid Id { get; set; }

        public Guid BillStreamId { get; set; }

        public string Status { get; set; } =
            string.Empty;

        public DateTimeOffset CreatedAtUtc { get; set; }

        public DateTimeOffset UpdatedAtUtc { get; set; }
    }
}
