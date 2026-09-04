using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class BillStatementUploadBoundaryTests
    : IClassFixture<BillWatchApiFactory>
{
    private readonly BillWatchApiFactory _factory;

    public BillStatementUploadBoundaryTests(
        BillWatchApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Upload_AcceptsPngBySignatureAndDoesNotTrustClaimedMimeType()
    {
        using var client = _factory.CreateHttpsClient();
        await AuthorizeNewUserAsync(client);
        var billStreamId = await CreateBillStreamAsync(client);

        var pngBytes = CreatePngBytes();

        using var content = CreateMultipartFile(
            pngBytes,
            "utility-statement.png",
            "application/pdf");

        using var response = await client.PostAsync(
            $"/api/bill-streams/{billStreamId:D}/statement-uploads",
            content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await ReadUploadAsync(response);

        Assert.Equal(billStreamId, result.BillStreamId);
        Assert.Equal("image/png", result.MediaType);
        Assert.Equal(".png", result.FileExtension);
        Assert.Equal(pngBytes.LongLength, result.SizeBytes);
        Assert.Equal("Uploaded", result.Status);

        await AssertResponseDoesNotExposeStorageOrOriginalNameAsync(
            response,
            "utility-statement.png");
    }

    [Theory]
    [InlineData("statement.jpg")]
    [InlineData("statement.jpeg")]
    public async Task Upload_AcceptsJpegExtensionsAndNormalizesStoredType(
        string fileName)
    {
        using var client = _factory.CreateHttpsClient();
        await AuthorizeNewUserAsync(client);
        var billStreamId = await CreateBillStreamAsync(client);

        var jpegBytes = CreateJpegSignatureBytes();

        using var content = CreateMultipartFile(
            jpegBytes,
            fileName,
            "application/octet-stream");

        using var response = await client.PostAsync(
            $"/api/bill-streams/{billStreamId:D}/statement-uploads",
            content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await ReadUploadAsync(response);

        Assert.Equal("image/jpeg", result.MediaType);
        Assert.Equal(".jpg", result.FileExtension);
        Assert.Equal(jpegBytes.LongLength, result.SizeBytes);

        await AssertResponseDoesNotExposeStorageOrOriginalNameAsync(
            response,
            fileName);
    }

    [Fact]
    public async Task Upload_RejectsExtensionThatDoesNotMatchDetectedSignature()
    {
        using var client = _factory.CreateHttpsClient();
        await AuthorizeNewUserAsync(client);
        var billStreamId = await CreateBillStreamAsync(client);

        using var content = CreateMultipartFile(
            CreatePngBytes(),
            "disguised.pdf",
            "application/pdf");

        using var response = await client.PostAsync(
            $"/api/bill-streams/{billStreamId:D}/statement-uploads",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(
            "extension does not match",
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "storageKey",
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "physicalPath",
            body,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upload_RejectsUnsupportedExtensionEvenWhenFileSignatureIsSupported()
    {
        using var client = _factory.CreateHttpsClient();
        await AuthorizeNewUserAsync(client);
        var billStreamId = await CreateBillStreamAsync(client);

        using var content = CreateMultipartFile(
            CreatePdfBytes(),
            "statement.txt",
            "application/pdf");

        using var response = await client.PostAsync(
            $"/api/bill-streams/{billStreamId:D}/statement-uploads",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(
            "Only PDF, JPG, JPEG, and PNG",
            body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_RejectsEmptyFileBeforePersistence()
    {
        using var client = _factory.CreateHttpsClient();
        await AuthorizeNewUserAsync(client);
        var billStreamId = await CreateBillStreamAsync(client);

        using var content = CreateMultipartFile(
            [],
            "empty.pdf",
            "application/pdf");

        using var response = await client.PostAsync(
            $"/api/bill-streams/{billStreamId:D}/statement-uploads",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(
            "empty",
            body,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upload_RejectsMultipartRequestWithoutFile()
    {
        using var client = _factory.CreateHttpsClient();
        await AuthorizeNewUserAsync(client);
        var billStreamId = await CreateBillStreamAsync(client);

        using var content = new MultipartFormDataContent();

        using var response = await client.PostAsync(
            $"/api/bill-streams/{billStreamId:D}/statement-uploads",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_DoesNotEchoUntrustedOriginalFilenameOrPathComponents()
    {
        using var client = _factory.CreateHttpsClient();
        await AuthorizeNewUserAsync(client);
        var billStreamId = await CreateBillStreamAsync(client);

        const string untrustedFileName =
            "../../private-account-number-1234.png";

        using var content = CreateMultipartFile(
            CreatePngBytes(),
            untrustedFileName,
            "image/png");

        using var response = await client.PostAsync(
            $"/api/bill-streams/{billStreamId:D}/statement-uploads",
            content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await AssertResponseDoesNotExposeStorageOrOriginalNameAsync(
            response,
            "private-account-number-1234.png");
    }

    private static async Task AuthorizeNewUserAsync(
        HttpClient client)
    {
        var session = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, session);
    }

    private static async Task<Guid> CreateBillStreamAsync(
        HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/bill-streams",
            new
            {
                providerName = $"Boundary Test {Guid.NewGuid():N}",
                category = "Internet"
            });

        response.EnsureSuccessStatusCode();

        var billStream = await response.Content
            .ReadFromJsonAsync<BillStreamPayload>();

        return billStream?.Id
            ?? throw new InvalidOperationException(
                "The test bill stream response was empty.");
    }

    private static MultipartFormDataContent CreateMultipartFile(
        byte[] bytes,
        string fileName,
        string mediaType)
    {
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        multipart.Add(fileContent, "file", fileName);
        return multipart;
    }

    private static async Task<BillStatementUploadPayload> ReadUploadAsync(
        HttpResponseMessage response)
    {
        return await response.Content
            .ReadFromJsonAsync<BillStatementUploadPayload>()
            ?? throw new InvalidOperationException(
                "The statement upload response was empty.");
    }

    private static async Task AssertResponseDoesNotExposeStorageOrOriginalNameAsync(
        HttpResponseMessage response,
        string originalFileName)
    {
        var responseText = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(
            "storageKey",
            responseText,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "physicalPath",
            responseText,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            originalFileName,
            responseText,
            StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] CreatePdfBytes()
    {
        return Encoding.ASCII.GetBytes(
            "%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF");
    }

    private static byte[] CreatePngBytes()
    {
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    }

    private static byte[] CreateJpegSignatureBytes()
    {
        return
        [
            0xFF, 0xD8, 0xFF, 0xE0,
            0x00, 0x10, 0x4A, 0x46,
            0x49, 0x46, 0x00, 0x01
        ];
    }

    private sealed class BillStreamPayload
    {
        public Guid Id { get; set; }
    }

    private sealed class BillStatementUploadPayload
    {
        public Guid Id { get; set; }
        public Guid BillStreamId { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public string FileExtension { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; }
    }
}
