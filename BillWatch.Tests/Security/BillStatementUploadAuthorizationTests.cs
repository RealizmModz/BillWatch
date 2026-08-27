using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class BillStatementUploadAuthorizationTests
    : IClassFixture<BillWatchApiFactory>
{
    private readonly BillWatchApiFactory _factory;

    public BillStatementUploadAuthorizationTests(
        BillWatchApiFactory factory)
    {
        _factory =
            factory;
    }

    [Fact]
    public async Task
        Upload_RequiresAuthentication()
    {
        using var client =
            _factory.CreateHttpsClient();

        using var content =
            CreateMultipartFile(
                CreatePdfBytes(),
                "statement.pdf",
                "application/pdf");

        using var response =
            await client.PostAsync(
                $"/api/bill-streams/{Guid.NewGuid()}/statement-uploads",
                content);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task
        Upload_CannotTargetAnotherUsersBillStream()
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

        TestUserAuthentication.Authorize(
            client,
            attacker);

        using var content =
            CreateMultipartFile(
                CreatePdfBytes(),
                "statement.pdf",
                "application/pdf");

        using var response =
            await client.PostAsync(
                $"/api/bill-streams/{billStream.Id}/statement-uploads",
                content);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task
        Upload_RejectsFakePdf()
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

        var fakePdf =
            Encoding.UTF8.GetBytes(
                "This is definitely not a PDF.");

        using var content =
            CreateMultipartFile(
                fakePdf,
                "statement.pdf",
                "application/pdf");

        using var response =
            await client.PostAsync(
                $"/api/bill-streams/{billStream.Id}/statement-uploads",
                content);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task
        Upload_RejectsFileOverStorageLimit()
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

        var oversizedFile =
            new byte[
                (15 * 1024 * 1024) + 1];

        oversizedFile[0] = 0x25;
        oversizedFile[1] = 0x50;
        oversizedFile[2] = 0x44;
        oversizedFile[3] = 0x46;
        oversizedFile[4] = 0x2D;

        using var content =
            CreateMultipartFile(
                oversizedFile,
                "statement.pdf",
                "application/pdf");

        using var response =
            await client.PostAsync(
                $"/api/bill-streams/{billStream.Id}/statement-uploads",
                content);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task
        Upload_AcceptsSupportedPdfSignature()
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

        var pdfBytes =
            CreatePdfBytes();

        using var content =
            CreateMultipartFile(
                pdfBytes,
                "utility-statement.pdf",
                "application/pdf");

        using var response =
            await client.PostAsync(
                $"/api/bill-streams/{billStream.Id}/statement-uploads",
                content);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var result =
            await response.Content
                .ReadFromJsonAsync<
                    BillStatementUploadPayload>();

        Assert.NotNull(
            result);

        Assert.NotEqual(
            Guid.Empty,
            result.Id);

        Assert.Equal(
            billStream.Id,
            result.BillStreamId);

        Assert.Equal(
            "application/pdf",
            result.MediaType);

        Assert.Equal(
            ".pdf",
            result.FileExtension);

        Assert.Equal(
            pdfBytes.LongLength,
            result.SizeBytes);

        Assert.Equal(
            "Uploaded",
            result.Status);

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
                        $"Upload Test {Guid.NewGuid():N}",

                    category =
                        "Internet"
                });

        response.EnsureSuccessStatusCode();

        var billStream =
            await response.Content
                .ReadFromJsonAsync<
                    BillStreamPayload>();

        return billStream
            ?? throw new InvalidOperationException(
                "The test bill stream response was empty.");
    }

    private static MultipartFormDataContent
        CreateMultipartFile(
            byte[] bytes,
            string fileName,
            string mediaType)
    {
        var multipart =
            new MultipartFormDataContent();

        var fileContent =
            new ByteArrayContent(
                bytes);

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue(
                mediaType);

        multipart.Add(
            fileContent,
            "file",
            fileName);

        return multipart;
    }

    private static byte[]
        CreatePdfBytes()
    {
        return Encoding.ASCII.GetBytes(
            "%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF");
    }

    private sealed class BillStreamPayload
    {
        public Guid Id { get; set; }
    }

    private sealed class BillStatementUploadPayload
    {
        public Guid Id { get; set; }

        public Guid BillStreamId { get; set; }

        public string MediaType { get; set; } =
            string.Empty;

        public string FileExtension { get; set; } =
            string.Empty;

        public long SizeBytes { get; set; }

        public string Status { get; set; } =
            string.Empty;

        public DateTimeOffset CreatedAtUtc { get; set; }
    }
}