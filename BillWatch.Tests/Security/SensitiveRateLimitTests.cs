using System.Net;
using System.Net.Http.Json;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class SensitiveRateLimitTests
{
    [Fact]
    public async Task AuthenticationLimiter_RejectsTwentyFirstAnonymousRequest()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            using var response =
                await SendInvalidLoginAsync(
                    client,
                    attempt);

            Assert.NotEqual(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
        }

        using var limitedResponse =
            await SendInvalidLoginAsync(
                client,
                21);

        AssertRateLimited(limitedResponse);
    }

    [Fact]
    public async Task AccountExportLimiter_IsUserPartitioned()
    {
        using var factory =
            new BillWatchApiFactory();

        using var firstClient =
            factory.CreateHttpsClient();

        using var secondClient =
            factory.CreateHttpsClient();

        var firstSession =
            await TestUserAuthentication.RegisterAndLoginAsync(
                firstClient);

        var secondSession =
            await TestUserAuthentication.RegisterAndLoginAsync(
                secondClient);

        TestUserAuthentication.Authorize(
            firstClient,
            firstSession);

        TestUserAuthentication.Authorize(
            secondClient,
            secondSession);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response =
                await firstClient.GetAsync(
                    "/api/account/export");

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);
        }

        using var limitedResponse =
            await firstClient.GetAsync(
                "/api/account/export");

        AssertRateLimited(limitedResponse);

        using var otherUserResponse =
            await secondClient.GetAsync(
                "/api/account/export");

        Assert.Equal(
            HttpStatusCode.OK,
            otherUserResponse.StatusCode);
    }

    [Fact]
    public async Task SubscriptionRedemptionLimiter_RejectsSixthRequest()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        TestUserAuthentication.Authorize(
            client,
            session);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response =
                await client.PostAsJsonAsync(
                    "/api/subscription/access-keys/redeem",
                    new
                    {
                        accessKey =
                            $"invalid-access-key-{attempt}"
                    });

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);
        }

        using var limitedResponse =
            await client.PostAsJsonAsync(
                "/api/subscription/access-keys/redeem",
                new
                {
                    accessKey =
                        "invalid-access-key-limited"
                });

        AssertRateLimited(limitedResponse);
    }

    [Fact]
    public async Task StatementUploadLimiter_RejectsThirteenthRequest()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        TestUserAuthentication.Authorize(
            client,
            session);

        var billStreamId =
            Guid.NewGuid();

        for (var attempt = 1; attempt <= 12; attempt++)
        {
            using var response =
                await SendEmptyStatementUploadAsync(
                    client,
                    billStreamId);

            Assert.NotEqual(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
        }

        using var limitedResponse =
            await SendEmptyStatementUploadAsync(
                client,
                billStreamId);

        AssertRateLimited(limitedResponse);
    }

    [Fact]
    public async Task StatementDownloadLimiter_RejectsThirtyFirstRequest()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        TestUserAuthentication.Authorize(
            client,
            session);

        var billStreamId =
            Guid.NewGuid();

        var uploadId =
            Guid.NewGuid();

        var route =
            $"/api/bill-streams/{billStreamId:D}/statement-uploads/{uploadId:D}/file";

        for (var attempt = 1; attempt <= 30; attempt++)
        {
            using var response =
                await client.GetAsync(route);

            Assert.NotEqual(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
        }

        using var limitedResponse =
            await client.GetAsync(route);

        AssertRateLimited(limitedResponse);
    }

    private static async Task<HttpResponseMessage> SendInvalidLoginAsync(
        HttpClient client,
        int attempt)
    {
        return await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                email =
                    $"missing-{attempt}@billwatch.local",

                password =
                    "BillWatch!Invalid123"
            });
    }

    private static async Task<HttpResponseMessage> SendEmptyStatementUploadAsync(
        HttpClient client,
        Guid billStreamId)
    {
        using var content =
            new MultipartFormDataContent();

        return await client.PostAsync(
            $"/api/bill-streams/{billStreamId:D}/statement-uploads",
            content);
    }

    private static void AssertRateLimited(
        HttpResponseMessage response)
    {
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            response.StatusCode);

        Assert.True(
            response.Headers.TryGetValues(
                "Retry-After",
                out var retryAfterValues));

        var retryAfter =
            Assert.Single(
                retryAfterValues);

        Assert.True(
            int.TryParse(
                retryAfter,
                out var retryAfterSeconds));

        Assert.True(
            retryAfterSeconds > 0);
    }
}
