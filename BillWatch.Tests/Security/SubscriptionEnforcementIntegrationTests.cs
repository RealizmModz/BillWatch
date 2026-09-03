using System.Net;
using BillWatch.Tests.Infrastructure;

namespace BillWatch.Tests.Security;

public sealed class SubscriptionEnforcementIntegrationTests
{
    [Fact]
    public async Task EnabledGate_BlocksFinancialRoutesButKeepsSubscriptionEscapeHatch()
    {
        await using var factory =
            BillWatchApiFactory.WithSubscriptionEnforcement();
        using var client = factory.CreateHttpsClient();
        var user = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, user);

        using var financialResponse =
            await client.GetAsync("/api/bill-streams");
        using var subscriptionResponse =
            await client.GetAsync("/api/subscription");

        Assert.Equal(HttpStatusCode.Forbidden, financialResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, subscriptionResponse.StatusCode);
    }
}
