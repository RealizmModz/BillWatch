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
        var billStreamId =
            Guid.NewGuid();

        var uploadId =
            Guid.NewGuid();

        yield return
        [
            "GET",
            "/api/bank-connections"
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
            "GET",
            $"/api/bill-streams/{billStreamId}/statement-uploads/{uploadId}"
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
            "/api/plaid/accounts/sync"
        ];

        yield return
        [
            "POST",
            "/api/plaid/transactions/sync"
        ];

        yield return
        [
            "DELETE",
            "/api/account"
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
}