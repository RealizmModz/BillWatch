using System.Net;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class AnonymousFinancialEndpointAuthorizationTests
    : IClassFixture<BillWatchApiFactory>
{
    private readonly BillWatchApiFactory
        _factory;

    public AnonymousFinancialEndpointAuthorizationTests(
        BillWatchApiFactory factory)
    {
        _factory =
            factory;
    }

    public static IEnumerable<object[]>
        ProtectedEndpoints()
    {
        var alertId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var uploadId =
            Guid.NewGuid();

        var connectionId =
            Guid.NewGuid();

        yield return
        [
            "GET",
            "/api/bank-connections"
        ];

        yield return
        [
            "DELETE",
            $"/api/bank-connections/{connectionId}"
        ];

        yield return
        [
            "GET",
            "/api/bank-accounts"
        ];

        yield return
        [
            "GET",
            "/api/bank-transactions"
        ];

        yield return
        [
            "GET",
            "/api/bill-streams"
        ];

        yield return
        [
            "GET",
            $"/api/bill-streams/{billStreamId}"
        ];

        yield return
        [
            "GET",
            "/api/alerts"
        ];

        yield return
        [
            "POST",
            $"/api/alerts/{alertId}/read"
        ];

        yield return
        [
            "POST",
            $"/api/alerts/{alertId}/dismiss"
        ];

        yield return
        [
            "POST",
            $"/api/bill-streams/{billStreamId}/statement-uploads"
        ];

        yield return
        [
            "GET",
            $"/api/bill-streams/{billStreamId}/statement-uploads/{uploadId}"
        ];

        yield return
        [
            "GET",
            $"/api/bill-streams/{billStreamId}/statement-uploads/{uploadId}/file"
        ];

        yield return
        [
            "POST",
            "/api/bill-discovery/run"
        ];

        yield return
        [
            "POST",
            "/api/bill-monitoring/refresh"
        ];

        yield return
        [
            "POST",
            "/api/plaid/link-token"
        ];

        yield return
        [
            "POST",
            $"/api/plaid/connections/{connectionId}/update-link-token"
        ];

        yield return
        [
            "POST",
            "/api/plaid/accounts/sync"
        ];

        yield return
        [
            "POST",
            "/api/plaid/transactions/sync"
        ];

        yield return
        [
            "GET",
            "/api/account/export"
        ];

        yield return
        [
            "DELETE",
            "/api/account"
        ];

        yield return
        [
            "GET",
            "/api/subscription"
        ];

        yield return
        [
            "GET",
            "/api/subscription/plans"
        ];

        yield return
        [
            "POST",
            "/api/subscription/checkout"
        ];

        yield return
        [
            "POST",
            "/api/subscription/billing-portal"
        ];

        yield return
        [
            "POST",
            "/api/subscription/sync"
        ];

        yield return
        [
            "POST",
            "/api/subscription/access-keys/redeem"
        ];
    }

    [Theory]
    [MemberData(
        nameof(ProtectedEndpoints))]
    public async Task FinancialEndpoints_RejectAnonymousRequests(
        string method,
        string route)
    {
        using var client =
            _factory.CreateHttpsClient();

        using var request =
            new HttpRequestMessage(
                new HttpMethod(
                    method),
                route);

        using var response =
            await client.SendAsync(
                request);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task StripeWebhook_RemainsAnonymousButHiddenWhenBillingIsUnconfigured()
    {
        using var client =
            _factory.CreateHttpsClient();

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/subscription/webhooks/stripe")
            {
                Content =
                    new StringContent(
                        "{}")
            };

        using var response =
            await client.SendAsync(
                request);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }
}
