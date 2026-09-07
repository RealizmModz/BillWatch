using System.Net;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class ApiResponseCacheSecurityTests
{
    [Theory]
    [InlineData("/api/bank-accounts")]
    [InlineData("/api/bank-connections")]
    [InlineData("/api/bank-transactions")]
    [InlineData("/api/bill-streams")]
    [InlineData("/api/alerts")]
    [InlineData("/api/account/export")]
    [InlineData("/api/subscription")]
    public async Task ProtectedApiResponses_AreNoStore(
        string route)
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(route);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        AssertNoStore(response);
    }

    [Fact]
    public async Task HiddenStripeWebhookResponse_IsNoStore()
    {
        using var factory =
            new BillWatchApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsync(
                "/api/subscription/webhooks/stripe",
                new StringContent("{}"));

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        AssertNoStore(response);
    }

    private static void AssertNoStore(
        HttpResponseMessage response)
    {
        Assert.True(
            response.Headers.TryGetValues(
                "Cache-Control",
                out var cacheControl));

        Assert.Contains(
            cacheControl,
            value =>
                value.Contains(
                    "no-store",
                    StringComparison.OrdinalIgnoreCase));

        Assert.True(
            response.Headers.TryGetValues(
                "Pragma",
                out var pragma));

        Assert.Contains(
            pragma,
            value =>
                value.Contains(
                    "no-cache",
                    StringComparison.OrdinalIgnoreCase));
    }
}
