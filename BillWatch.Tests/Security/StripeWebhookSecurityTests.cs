using System.Net;
using System.Text;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class StripeWebhookSecurityTests
{
    private const int MaximumPayloadBytes =
        256 * 1024;

    [Fact]
    public async Task ConfiguredWebhook_RejectsOversizedPayloadBeforeSignatureProcessing()
    {
        using var factory =
            BillWatchApiFactory.WithStripeBilling();

        using var client =
            factory.CreateHttpsClient();

        var oversizedPayload =
            new string(
                'x',
                MaximumPayloadBytes + 1);

        using var request =
            CreateWebhookRequest(
                new StringContent(
                    oversizedPayload,
                    Encoding.UTF8,
                    "application/json"));

        using var response =
            await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
    }

    [Fact]
    public async Task ConfiguredWebhook_RejectsOversizedChunkedPayloadWithoutContentLength()
    {
        using var factory =
            BillWatchApiFactory.WithStripeBilling();

        using var client =
            factory.CreateHttpsClient();

        using var request =
            CreateWebhookRequest(
                new UnknownLengthContent(
                    MaximumPayloadBytes + 1));

        Assert.Null(
            request.Content!.Headers.ContentLength);

        using var response =
            await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
    }

    [Fact]
    public async Task ConfiguredWebhook_MaximumSizedPayloadReachesSignatureValidation()
    {
        using var factory =
            BillWatchApiFactory.WithStripeBilling();

        using var client =
            factory.CreateHttpsClient();

        using var request =
            CreateWebhookRequest(
                new UnknownLengthContent(
                    MaximumPayloadBytes));

        using var response =
            await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task ConfiguredWebhook_RejectsInvalidSignatureWithoutEchoingPayload()
    {
        using var factory =
            BillWatchApiFactory.WithStripeBilling();

        using var client =
            factory.CreateHttpsClient();

        const string sensitiveMarker =
            "must-not-be-echoed";

        using var request =
            CreateWebhookRequest(
                new StringContent(
                    $"{{\"marker\":\"{sensitiveMarker}\"}}",
                    Encoding.UTF8,
                    "application/json"));

        using var response =
            await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        var responseBody =
            await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(
            sensitiveMarker,
            responseBody,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "invalid-test-signature",
            responseBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnconfiguredWebhook_RemainsHidden()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/subscription/webhooks/stripe")
            {
                Content =
                    new StringContent(
                        "{}",
                        Encoding.UTF8,
                        "application/json")
            };

        using var response =
            await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    private static HttpRequestMessage CreateWebhookRequest(
        HttpContent content)
    {
        var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/subscription/webhooks/stripe")
            {
                Content =
                    content
            };

        request.Headers.TryAddWithoutValidation(
            "Stripe-Signature",
            "invalid-test-signature");

        return request;
    }

    private sealed class UnknownLengthContent(
        int byteCount)
        : HttpContent
    {
        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            var buffer =
                new byte[8192];

            Array.Fill(
                buffer,
                (byte)'x');

            var remaining =
                byteCount;

            while (remaining > 0)
            {
                var writeCount =
                    Math.Min(
                        remaining,
                        buffer.Length);

                await stream.WriteAsync(
                    buffer.AsMemory(
                        0,
                        writeCount));

                remaining -=
                    writeCount;
            }
        }

        protected override bool TryComputeLength(
            out long length)
        {
            length = 0;
            return false;
        }
    }
}
