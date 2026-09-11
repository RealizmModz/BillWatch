using System.Net;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class SubscriptionEnforcementIntegrationTests
{
    [Theory]
    [InlineData("GET", "/api/bank-accounts")]
    [InlineData("GET", "/api/bank-transactions")]
    [InlineData("GET", "/api/bill-streams")]
    [InlineData("GET", "/api/alerts")]
    [InlineData("GET", "/api/account/preferences")]
    [InlineData("POST", "/api/bill-discovery/run")]
    [InlineData("POST", "/api/bill-monitoring/refresh")]
    [InlineData("POST", "/api/plaid/link-token")]
    public async Task EnabledGate_BlocksProtectedFinancialRoutes(
        string method,
        string route)
    {
        await using var factory = BillWatchApiFactory.WithSubscriptionEnforcement();
        using var client = factory.CreateHttpsClient();
        var user = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, user);
        using var request = new HttpRequestMessage(new HttpMethod(method), route);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EnabledGate_KeepsSubscriptionRecoveryAvailable()
    {
        await using var factory = BillWatchApiFactory.WithSubscriptionEnforcement();
        using var client = factory.CreateHttpsClient();
        var user = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, user);

        using var response = await client.GetAsync("/api/subscription");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
