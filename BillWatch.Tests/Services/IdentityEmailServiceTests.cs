using System.Net;
using System.Text;
using BillWatch.API.Data.Entities;
using BillWatch.API.Services.Identity;
using Microsoft.Extensions.Options;

namespace BillWatch.Tests.Services;

public sealed class IdentityEmailServiceTests
{
    [Fact]
    public void Validator_AllowsDisabledConfiguration()
    {
        var validator =
            new IdentityEmailOptionsValidator();

        var result =
            validator.Validate(
                null,
                new IdentityEmailOptions
                {
                    Enabled = false
                });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validator_RejectsEnabledConfigurationWithoutApiKey()
    {
        var validator =
            new IdentityEmailOptionsValidator();

        var result =
            validator.Validate(
                null,
                new IdentityEmailOptions
                {
                    Enabled = true,
                    FromAddress = "security@billbeacon.net",
                    FromName = "BillWatch",
                    PublicWebBaseUrl = "https://billbeacon.net"
                });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Validator_AcceptsEnabledSecureConfiguration()
    {
        var validator =
            new IdentityEmailOptionsValidator();

        var result =
            validator.Validate(
                null,
                CreateEnabledOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task PasswordResetCode_ProducesWebResetLinkAndBearerRequest()
    {
        var handler =
            new RecordingHandler();

        using var httpClient =
            new HttpClient(handler)
            {
                BaseAddress =
                    new Uri(
                        "https://api.resend.com/")
            };

        var sender =
            new ResendIdentityEmailSender(
                httpClient,
                Options.Create(
                    CreateEnabledOptions()));

        await sender.SendPasswordResetCodeAsync(
            new ApplicationUser(),
            "person@example.com",
            "code+/= value");

        Assert.NotNull(handler.Request);
        Assert.Equal(
            HttpMethod.Post,
            handler.Request!.Method);
        Assert.Equal(
            "https://api.resend.com/emails",
            handler.Request.RequestUri?.AbsoluteUri);
        Assert.Equal(
            "Bearer",
            handler.Request.Headers.Authorization?.Scheme);
        Assert.Equal(
            "test-resend-key",
            handler.Request.Headers.Authorization?.Parameter);

        Assert.Contains(
            "https://billbeacon.net/reset-password?email=person%40example.com&amp;code=code%2B%2F%3D%20value",
            handler.Body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledSender_FailsBeforeNetworkRequest()
    {
        var handler =
            new RecordingHandler();

        using var httpClient =
            new HttpClient(handler)
            {
                BaseAddress =
                    new Uri(
                        "https://api.resend.com/")
            };

        var sender =
            new ResendIdentityEmailSender(
                httpClient,
                Options.Create(
                    new IdentityEmailOptions
                    {
                        Enabled = false
                    }));

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => sender.SendPasswordResetCodeAsync(
                new ApplicationUser(),
                "person@example.com",
                "reset-code"));

        Assert.Null(handler.Request);
    }

    private static IdentityEmailOptions
        CreateEnabledOptions()
    {
        return new IdentityEmailOptions
        {
            Enabled = true,
            ApiKey = "test-resend-key",
            FromAddress = "security@billbeacon.net",
            FromName = "BillWatch",
            PublicWebBaseUrl = "https://billbeacon.net"
        };
    }

    private sealed class RecordingHandler
        : HttpMessageHandler
    {
        public HttpRequestMessage? Request
        {
            get;
            private set;
        }

        public string Body
        {
            get;
            private set;
        } = string.Empty;

        protected override async Task<
            HttpResponseMessage>
            SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
        {
            Request = request;

            if (request.Content is not null)
            {
                Body =
                    await request.Content.ReadAsStringAsync(
                        cancellationToken);
            }

            return new HttpResponseMessage(
                HttpStatusCode.OK)
            {
                Content =
                    new StringContent(
                        "{}",
                        Encoding.UTF8,
                        "application/json")
            };
        }
    }
}
